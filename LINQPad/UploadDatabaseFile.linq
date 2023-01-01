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

var vacuumedDbPath = @"C:\Users\woute\Downloads\2.sqlite";

var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
var container = new BlobContainerClient(connectionString, containerName);
	
var StateDbsFolderName = "states";
var versionUtc = DateTime.Now;

var blobName = $"{StateDbsFolderName}/{versionUtc:s}";
var bbc = container.GetBlockBlobClient(blobName);
await using (var ss = File.OpenRead(vacuumedDbPath)) //do not convert to inline using; the File.Delete will fail
{
	await using var ts = await bbc.OpenWriteAsync(overwrite: true);
	await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
}

await bbc.SetAccessTierAsync(AccessTier.Cool);
await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType });