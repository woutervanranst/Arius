<Query Kind="Statements">
  <NuGetReference>Arius.Core</NuGetReference>
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <Namespace>Azure.Storage.Blobs</Namespace>
  <Namespace>Azure.Storage.Blobs.Specialized</Namespace>
  <Namespace>Arius.Core.Services</Namespace>
  <Namespace>Azure.Storage.Blobs.Models</Namespace>
</Query>

var accountName = "vanranstarius2";
var accountKey = Util.GetPassword("accountkey");
var passphrase = Util.GetPassword("passphrase");

var containerName = "series";

var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
var container = new BlobContainerClient(connectionString, containerName);

var lastStateBlobName = await container.GetBlobsAsync(prefix: "states/")
				.Select(bi => bi.Name)
				.OrderBy(n => n)
				.LastOrDefaultAsync();

var cs = container.GetBlobBaseClient(lastStateBlobName);

await using (var ss = await cs.OpenReadAsync())
{
	var fn = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "db.sqlite");
	await using (var ts = File.OpenWrite(fn))
	{
		await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
	}
	
	fn.Dump();
}