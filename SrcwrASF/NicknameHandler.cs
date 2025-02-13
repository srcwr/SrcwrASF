using System.Collections.ObjectModel;
using System.Linq;
using SteamKit2;
using SteamKit2.Internal;

namespace SrcwrASF;

/*
[Flags]
public enum EPersonaChange {
	Name = 1,
	Status = 2,
	ComeOnline = 4,
	GoneOffline = 8,
	GamePlayed = 16,
	GameServer = 64,
	Avatar = 128,
	JoinedSource = 256,
	LeftSource = 512,
	RelationshipChanged = 1024,
	NameFirstSet = 2048,
	FacebookInfo = 4096,
	Nickname = 8192,
	SteamLevel = 16384,
}
*/

internal sealed class NicknameHandler : ClientMsgHandler {
	public AsyncJob<SetPlayerNameCallback> SetPlayerNickname(SteamID steamID, string nickname = "") {
		ClientMsgProtobuf<CMsgClientSetPlayerNickname> msg = new(EMsg.AMClientSetPlayerNickname) {
			SourceJobID = Client.GetNextJobID()
		};
		msg.Body.steamid = steamID;
		msg.Body.nickname = nickname;
		Client.Send(msg);
		return new AsyncJob<SetPlayerNameCallback>(Client, msg.SourceJobID);
	}

	public sealed class SetPlayerNameCallback : CallbackMsg {
		public EResult Result { get; private set; }

		internal SetPlayerNameCallback(JobID jobID, CMsgClientSetPlayerNicknameResponse response) {
			JobID = jobID;
			Result = (EResult) response.eresult;
		}
	}

	public override void HandleMsg(IPacketMsg packetMsg) {
		if (packetMsg.MsgType == EMsg.ClientPlayerNicknameList) {
			ClientMsgProtobuf<CMsgClientPlayerNicknameList> nicknameList = new(packetMsg);
			Client.PostCallback(new PlayerNicknameListCallback(nicknameList.Body));
		} else if (packetMsg.MsgType is EMsg.AMClientSetPlayerNicknameResponse) {
			ClientMsgProtobuf<CMsgClientSetPlayerNicknameResponse> response = new(packetMsg);
			Client.PostCallback(new SetPlayerNameCallback(response.TargetJobID, response.Body));
		}
		/*else if (packetMsg.MsgType == EMsg.ClientPersonaState) {
			ClientMsgProtobuf<CMsgClientPersonaState> personaState = new(packetMsg);
			ASF.ArchiLogger.LogGenericInfo("status_flags = " + personaState.Body.status_flags.ToString(CultureInfo.InvariantCulture));
			ASF.ArchiLogger.LogGenericInfo("status_flags (EClientPersonaStateFlag) = " + ((EClientPersonaStateFlag)personaState.Body.status_flags).ToString());
			ASF.ArchiLogger.LogGenericInfo("status_flags (EPersonaChange) = " + ((EPersonaChange)personaState.Body.status_flags).ToString());
		}*/
	}

	public sealed class PlayerNicknameListCallback : CallbackMsg {
		public bool Removal { get; private set; }
		public bool Incremental { get; private set; }
		public sealed class Player {
			public SteamID SteamID { get; private set; }
			public string Nickname { get; private set; }
			internal Player(CMsgClientPlayerNicknameList.PlayerNickname nickname) {
				SteamID = nickname.steamid;
				Nickname = nickname.nickname;
			}
		}
		public ReadOnlyCollection<Player> Players { get; private set; }

		internal PlayerNicknameListCallback(CMsgClientPlayerNicknameList msg) {
			Removal = msg.removal;
			Incremental = msg.incremental;
			Players = new ReadOnlyCollection<Player>(
				[.. msg.nicknames.Select(n => new Player(n))]
			);
		}
	}
}
