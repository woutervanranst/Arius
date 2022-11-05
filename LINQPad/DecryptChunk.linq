<Query Kind="Statements">
  <NuGetReference>Arius.Core</NuGetReference>
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <Namespace>Azure.Storage.Blobs</Namespace>
  <Namespace>Azure.Storage.Blobs.Specialized</Namespace>
  <Namespace>Arius.Core.Services</Namespace>
  <Namespace>Azure.Storage.Blobs.Models</Namespace>
</Query>

var passphrase = "Zamoli12";

await using (var ss = File.Open(@"\\192.168.1.100\Video\zDownloaded Torrents\2eea4d7414d4c08ddc7aa5d0959365de6be56e73849734ce80edf449da0a4787", FileMode.Open))
{
	var fn = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "bla");
	await using (var ts = File.OpenWrite(fn))
	{
		await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
	}
	
	fn.Dump();
}