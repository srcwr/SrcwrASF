using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using SteamKit2;

namespace SrcwrASF;
internal static class SrcwrWebAPI {
	internal static ConcurrentDictionary<ulong, string> QueuedPersonaNames = new();
	internal static System.Threading.Lock QueuedPersonaNamesSubmitLock = new();
	internal static bool QueuedPersonaNamesSubmit;

	private static async Task DelayedPersonaNameSubmit() {
		await Task.Delay(3000).ConfigureAwait(false);
		List<ResponsePlayer> players = [];
		lock (QueuedPersonaNamesSubmitLock) {
			QueuedPersonaNamesSubmit = false;
			List<ulong> steamid64s = [.. QueuedPersonaNames.Keys];
			foreach (ulong steamid64 in steamid64s) {
				if (QueuedPersonaNames.TryRemove(steamid64, out string? personaname)) {
					players.Add(new ResponsePlayer { SteamID64 = steamid64.ToString(CultureInfo.InvariantCulture), Name = personaname });
				}
			}
		}
		ASF.ArchiLogger.LogGenericInfo(JsonSerializer.Serialize(players));
		// TODO: http request to srcwr api endpoint
	}
	public static void SendPersonaName(SteamID steamid, string name) {
		QueuedPersonaNames[steamid] = name;
		lock (QueuedPersonaNamesSubmitLock) {
			if (!QueuedPersonaNamesSubmit) {
				QueuedPersonaNamesSubmit = true;
				_ = DelayedPersonaNameSubmit();
			}
		}
	}
}
