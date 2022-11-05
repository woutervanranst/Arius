<Query Kind="Statements">
  <NuGetReference>Arius.Core</NuGetReference>
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <Namespace>Azure.Storage.Blobs</Namespace>
  <Namespace>Azure.Storage.Blobs.Specialized</Namespace>
  <Namespace>Arius.Core.Services</Namespace>
  <Namespace>Azure.Storage.Blobs.Models</Namespace>
</Query>

var passphrase = "...";

await using (var ss = File.Open(@"...", FileMode.Open))
{
	var fn = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "bla");
	await using (var ts = File.OpenWrite(fn))
	{
		await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
	}
	
	fn.Dump();
}