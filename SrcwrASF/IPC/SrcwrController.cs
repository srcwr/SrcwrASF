using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Steam;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SteamKit2;

namespace SrcwrASF.IPC;

[Route("/Api/Srcwr")]
public sealed class SrcwrController : ArchiController {
	// test with (currently) non-existent Steam user 76561199960265727 / [U:1:1999999999]
	[EndpointSummary("Returns a Steam user's persona name (and caches it). Name can be an empty string if the fetch failed.")]
	[HttpGet("{botName:required}/GetPersonaName/{steamID64:required}")]
	[ProducesResponseType<GenericResponse<ResponsePlayer>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public async Task<ActionResult<ResponsePlayer>> GetPersonaName(string botName, string steamID64) {
		Bot? bot = Bot.GetBot(botName);
		if (bot == null) {
			return BadRequest(new GenericResponse(false, "Only pass one bot name please... or bot not found..."));
		}
		SteamID target;
		try {
			target = Convert.ToUInt64(steamID64, CultureInfo.InvariantCulture);
		} catch (Exception) {
			return BadRequest(new GenericResponse(false, "Invalid steamid64"));
		}
		string? personaname = await SrcwrASF.GetPersonaName(bot, target).ConfigureAwait(false);
		return Ok(new ResponsePlayer {
			SteamID64 = target.ConvertToUInt64().ToString(CultureInfo.InvariantCulture),
			Name = personaname ?? "",
		});
	}

	[EndpointSummary("Returns all of the Steam users on this bot's friends list.")]
	[HttpGet("{botName:required}/GetFriendsList")]
	[ProducesResponseType<GenericResponse<List<string>>>((int) HttpStatusCode.OK)]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.BadRequest)]
	public Task<ActionResult<List<string>>> GetFriendsList(string botName) {
		Bot? bot = Bot.GetBot(botName);
		return bot == null
			? Task.FromResult<ActionResult<List<string>>>(BadRequest(new GenericResponse(false, "Only pass one bot name please... or bot not found...")))
			: Task.FromResult<ActionResult<List<string>>>(Ok(SrcwrASF.GetFriendsList(bot)));
	}
}
