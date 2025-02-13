using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using SteamKit2;
using SteamKit2.Internal;

namespace SrcwrASF;

internal sealed class BotSettings {
	public bool AcceptAllFriendRequests;
	public bool SendThings;
}

public class ResponseFriend {
	[JsonInclude]
	public string SteamID64 { get; set; } = "";
	[JsonInclude]
	public string Name { get; set; } = "";
}

#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class SrcwrASF : IGitHubPluginUpdates, IBot, IBotFriendRequest, IBotMessage, IBotCommand2, IBotSteamClient, IBotModules {
	public string Name => nameof(SrcwrASF);
	public string RepositoryName => "srcwr/SrcwrASF";
	public Version Version => typeof(SrcwrASF).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	internal static ConcurrentDictionary<Bot, ConcurrentDictionary<ulong, string>> NicknameCache = new();
	internal static ConcurrentDictionary<Bot, NicknameHandler> NicknameHandlers = new();
	internal static ConcurrentDictionary<Bot, List<IDisposable>> CallbackSubscriptions = new();
	internal static ConcurrentDictionary<Bot, BotSettings> Settings = new();
	internal static ConcurrentDictionary<ulong, ConcurrentBag<TaskCompletionSource<bool>>> PersonaNameInquirers = new();

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"Hello {Name}!");
		return Task.CompletedTask;
	}

	// IBotModules
	public Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		BotSettings settings = new();
		if (additionalConfigProperties != null) {
			foreach ((string key, JsonElement value) in additionalConfigProperties) {
				switch (key) {
					case $"SrcwrAcceptAllFriendRequests" when value.ValueKind == JsonValueKind.True:
						settings.AcceptAllFriendRequests = true;
						break;
					case $"SrcwrSendThings" when value.ValueKind == JsonValueKind.True:
						settings.SendThings = true;
						break;
					default:
						break;
				}
			}
		}
		Settings[bot] = settings;
		return Task.CompletedTask;
	}

	// IBot
	public Task OnBotDestroy(Bot bot) {
		_ = NicknameCache.TryRemove(bot, out _);
		_ = NicknameHandlers.TryRemove(bot, out _);
		_ = CallbackSubscriptions.TryRemove(bot, out _);
		_ = Settings.TryRemove(bot, out _);
		return Task.CompletedTask;
	}
	// IBot
	public Task OnBotInit(Bot bot) {
		NicknameCache[bot] = new();
		return Task.CompletedTask;
	}

	// IBotFriendRequest
	// Always accept friend requests...
	public Task<bool> OnBotFriendRequest(Bot bot, ulong steamID) {
		if (Settings[bot].AcceptAllFriendRequests) {
			bot.ArchiLogger.LogGenericInfo("Received (& accepting) friend request from " + steamID.ToString(CultureInfo.InvariantCulture));
			return Task.FromResult(true);
		} else {
			bot.ArchiLogger.LogGenericInfo("Received friend request from " + steamID.ToString(CultureInfo.InvariantCulture));
			return Task.FromResult(false);
		}
	}

	// IBotMessage
	public Task<string?> OnBotMessage(Bot bot, ulong steamID, string message) => Task.FromResult<string?>(null);

	// IBotCommand2
	public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong executorSteamID = 0) {
		if (access < EAccess.Operator) {
			return null;
		}
		string command = args[0].ToUpperInvariant();
		if (command == "GETPERSONANAME") {
#pragma warning disable IDE0046 // Convert to conditional expression
			if (args.Length != 2) {
				return null;
			}
#pragma warning restore IDE0046 // Convert to conditional expression
			string? personaname = await GetPersonaName(bot, Convert.ToUInt64(args[1], CultureInfo.InvariantCulture)).ConfigureAwait(false);
			return personaname == null ? "Failed to get persona name!" : "Persona name: " + personaname;
		} else if (command == "GETSTEAMNICKNAMES") {
			List<ResponseFriend> friends = [];
			foreach (ulong steamid in NicknameCache[bot].Keys) {
				if (NicknameCache[bot].TryGetValue(steamid, out string? nickname)) {
					friends.Add(new ResponseFriend {
						SteamID64 = steamid.ToString(CultureInfo.InvariantCulture),
						Name = nickname,
					});
				}
			}
			return JsonSerializer.Serialize(friends);
		} else if (command == "SETSTEAMNICKNAME") {
			if (args.Length is < 2 or > 3) {
				return null;
			}
			SteamID target = Convert.ToUInt64(args[1], CultureInfo.InvariantCulture);
#pragma warning disable IDE0046 // Convert to conditional expression
			if (target.AccountType != EAccountType.Individual) {
				return "failed! SteamID Account type isn't Individual";
			}
#pragma warning restore IDE0046 // Convert to conditional expression
			return await SetPlayerNickName(bot, target, args.Length == 2 ? "" : args[2]).ConfigureAwait(false) ? "success!" : "failed!";
		} else if (command == "GETFRIENDSLIST") {
			return JsonSerializer.Serialize(GetFriendsList(bot));
		}
		return null;
	}
	public static List<string> GetFriendsList(Bot bot) {
		List<string> friends = [];
		for (int i = 0, friend_count = bot.SteamFriends.GetFriendCount(); i < friend_count; i++) {
			SteamID friendID = bot.SteamFriends.GetFriendByIndex(i);
			if (friendID.AccountType != EAccountType.Individual) {
				continue;
			}
			friends.Add(friendID.ConvertToUInt64().ToString(CultureInfo.InvariantCulture));
		}
		return friends;
	}
	public static async Task<string?> GetPersonaName(Bot bot, SteamID target) {
		if (target.AccountType != EAccountType.Individual) {
			return null;
		}
		string? personaname = bot.SteamFriends.GetFriendPersonaName(target);
		//bot.ArchiLogger.LogGenericInfo("personaname = " + personaname ?? "");
		if (personaname == null) {
			TaskCompletionSource<bool> tcs = new();
			if (PersonaNameInquirers.TryGetValue(target, out ConcurrentBag<TaskCompletionSource<bool>>? inquirers)) {
				inquirers.Add(tcs);
			} else {
				PersonaNameInquirers[target] = [tcs];
			}
			bot.SteamFriends.RequestFriendInfo(target, EClientPersonaStateFlag.PlayerName);
			if (await Task.WhenAny(tcs.Task, Task.Delay(3000)).ConfigureAwait(false) == tcs.Task) {
				//bot.ArchiLogger.LogGenericInfo("fetched name!");
				personaname = bot.SteamFriends.GetFriendPersonaName(target);
			}
		}
		return personaname;
		//return JsonSerializer.Serialize(new ResponseFriend { SteamID64 = target.ConvertToUInt64().ToString(CultureInfo.InvariantCulture), Name = personaname ?? "" });
	}
	public static async Task<bool> SetPlayerNickName(Bot bot, SteamID steamID, string nickname) {
		if (NicknameCache[bot].TryGetValue(steamID, out string? cached_nickname)) {
			if (cached_nickname == nickname) {
				return true;
			}
		}
		NicknameHandler.SetPlayerNameCallback callback = await NicknameHandlers[bot].SetPlayerNickname(steamID, nickname);
		if (callback.Result != EResult.OK) {
			bot.ArchiLogger.LogGenericError("Failed to set nickname: " + callback.Result.ToString());
			return false;
		}
		NicknameCache[bot][steamID] = nickname;
		return true;
	}

	// IBotSteamClient
	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
		CallbackSubscriptions[bot] =
		[
			callbackManager.Subscribe<SteamFriends.FriendAddedCallback>((callback) => OnFriendAdded(callback, bot)),
			callbackManager.Subscribe<SteamFriends.FriendsListCallback>((callback) => OnFriendsList(callback, bot)),
			callbackManager.Subscribe<SteamFriends.PersonaStateCallback>((callback) => OnPersonaState(callback, bot)),
			callbackManager.Subscribe<NicknameHandler.PlayerNicknameListCallback>((callback) => OnNicknameList(callback, bot)),
			callbackManager.SubscribeServiceNotification<PlayerClient, CPlayer_FriendNicknameChanged_Notification>((callback) => OnNicknameChanged(callback, bot)),
		];
		return Task.CompletedTask;
	}
	private static void OnFriendAdded(SteamFriends.FriendAddedCallback callback, Bot bot) {
		bot.ArchiLogger.LogGenericInfo("New friend added: " + callback.PersonaName);
		bot.ArchiLogger.LogGenericInfo("  ├─ SteamID3: " + callback.SteamID.Render(true));
		bot.ArchiLogger.LogGenericInfo("  └─ Profile url: https://steamcommunity.com/profiles/" + callback.SteamID.ConvertToUInt64().ToString(CultureInfo.InvariantCulture));
	}
	private static void OnFriendsList(SteamFriends.FriendsListCallback callback, Bot bot) {
		bool accept_all_friend_requests = Settings.TryGetValue(bot, out BotSettings? settings) && settings.AcceptAllFriendRequests;

		string shit = "";

		foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList) {
			if (friend.SteamID.AccountType != EAccountType.Individual) {
				continue;
			}

			if (friend.Relationship == EFriendRelationship.RequestRecipient) {
				bot.ArchiLogger.LogGenericInfo("Found friend request from " + friend.SteamID.Render(true));
				if (accept_all_friend_requests) {
					bot.SteamFriends.AddFriend(friend.SteamID);
				}
			} else if (friend.Relationship == EFriendRelationship.None) {
				// todo: removed...
			}

			shit += " " + friend.SteamID.Render(true);
		}

		if (shit.Length != 0) {
			bot.ArchiLogger.LogGenericInfo(shit);
		}
	}
	private static void OnNicknameChanged(SteamUnifiedMessages.ServiceMethodNotification<CPlayer_FriendNicknameChanged_Notification> callback, Bot bot) {
		SteamID steamID = new(callback.Body.accountid, EUniverse.Public, EAccountType.Individual);
		if (callback.Body.nickname.Length == 0) {
			_ = NicknameCache[bot].TryRemove(steamID, out _);
		} else {
			NicknameCache[bot][steamID] = callback.Body.nickname;
		}
		if (!callback.Body.is_echo_to_self) {
			// some other steam instance changed the nickname
			bot.ArchiLogger.LogGenericInfo(steamID.Render(true) + " = " + callback.Body.nickname);
		}
	}
	private static void OnNicknameList(NicknameHandler.PlayerNicknameListCallback callback, Bot bot) {
		foreach (NicknameHandler.PlayerNicknameListCallback.Player player in callback.Players) {
			if (callback.Removal || player.Nickname.Length == 0) {
				_ = NicknameCache[bot].TryRemove(player.SteamID, out _);
			} else {
				NicknameCache[bot][player.SteamID] = player.Nickname;
			}
		}
		/*
		string s = "Incremental = " + callback.Incremental.ToString() + "    Removal = " + callback.Removal.ToString();
		foreach (NicknameHandler.PlayerNicknameListCallback.Player player in callback.Players) {
			s = s + "\n  SteamID = " + player.SteamID.Render(true) + "\n  Nickname = " + player.Nickname;
		}
		bot.ArchiLogger.LogGenericInfo(s);
		*/
	}
	private static void OnPersonaState(SteamFriends.PersonaStateCallback callback, Bot bot) {
		if (callback.FriendID.AccountType != EAccountType.Individual) {
			return;
		}
		bot.ArchiLogger.LogGenericInfo("State change (" + callback.State.ToString() + ") (" + /*callback.StateFlags.ToString() + " " + callback.StatusFlags.ToString() +*/ "): " + callback.Name);
		if (PersonaNameInquirers.TryGetValue(callback.FriendID, out ConcurrentBag<TaskCompletionSource<bool>>? inquirers)) {
			while (inquirers.TryTake(out TaskCompletionSource<bool>? inquirer)) {
				inquirer.SetResult(true);
			}
		}
		//if (Settings[bot.BotName].SendThings) {
		//}
	}
	// IBotSteamClient
	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
#pragma warning disable IDE0022 // Use expression body for method
		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([
			NicknameHandlers[bot] = new(),
		]);
#pragma warning restore IDE0022 // Use expression body for method
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
