<Query Kind="Statements">
  <NuGetReference>Arius.Core</NuGetReference>
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <Namespace>Azure.Storage.Blobs</Namespace>
  <Namespace>Azure.Storage.Blobs.Specialized</Namespace>
  <Namespace>Arius.Core.Services</Namespace>
  <Namespace>Azure.Storage.Blobs.Models</Namespace>
</Query>

var accountKey = Util.GetPassword("accountkey");
var passphrase = Util.GetPassword("passphrase");

var accountName = "vanranstarius2";
var containerName = "series";

var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
var container = new BlobContainerClient(connectionString, containerName);

var hash = "d3066c90aae5ab0eef6f7af7c5134556045b593c3860210e9275fd2a03c20be1";
var cs = container.GetBlobBaseClient($"chunks/{hash}");

await using (var ss = await cs.OpenReadAsync())
{
	var fn = Path.GetTempFileName();
	await using (var ts = File.OpenWrite(fn))
	{
		await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
	}
	
	fn.Dump();
}