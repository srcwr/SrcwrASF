using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using JetBrains.Annotations;

namespace SrcwrASF;

#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class SrcwrASF : IGitHubPluginUpdates {
	public string Name => nameof(SrcwrASF);
	public string RepositoryName => "srcwr/SrcwrASF";
	public Version Version => typeof(SrcwrASF).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"Hello {Name}!");

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
