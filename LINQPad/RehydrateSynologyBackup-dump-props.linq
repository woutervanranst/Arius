<Query Kind="Statements">
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <Namespace>Azure.Storage.Blobs</Namespace>
  <Namespace>Azure.Storage.Blobs.Models</Namespace>
</Query>

var accountName = "...";
var accountKey = "....";
var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

//var logContainerName = "$logs";
//var logContainer = new BlobContainerClient(connectionString, logContainerName);

var bucketContainerName = "synologybackup";
var bucketContainer = new BlobContainerClient(connectionString, bucketContainerName);

var ka = bucketContainer.GetBlobs(prefix: $"synology_1.hbk/Pool/0/")
	//.Where(l => l.Properties.AccessTier == AccessTier.Archive)
	//.Take(20)
	.Where(l => l.Name.Contains(".bucket."))
	.Select(l => new { l.Name, l.Properties.AccessTier.Value, l.Properties.AccessTierChangedOn, l.Properties.LastModified, l.Properties.ContentLength })
	.ToArray();

ka.Dump();

Util.WriteCsv(ka, @"C:\Users\woute\Documents\blobs.txt");

//var logs = logContainer.GetBlobs(prefix: $"blob/2022/05/05/").Select(l => l.Name).ToArray();

//var totalSize = 0L;
//
//foreach (var logName in logs)
//{
//	logName.Dump();
//
//	var logBlobClient = logContainer.GetBlobClient(logName);
//	using var stream = logBlobClient.OpenRead();
//	using var reader = new StreamReader(stream);
//	var lines = reader.ReadToEnd().Split('\n').Select(l => l.Split(';'));
//
//	lines = lines
//		.Where(l => l.Count() >= 2 && l[2] == "GetBlobProperties");
//	var blobs = lines
//		.Where(l => l.Count() >= 11 && l[11]
//		.StartsWith("\"" + "https://....blob.core.windows.net:443/.../synology_1.hbk/Pool"))
//		.Select(l => l[11])
//		.Where(l => l.Contains("bucket"))
//		.Select(l => l.Replace("https://....blob.core.windows.net:443/.../", "").TrimStart('"').TrimEnd('"'));
//
//	foreach (var blobName in blobs)
//	{
//		var bucketBlobClient = bucketContainer.GetBlobClient(blobName);
//
//		if (!bucketBlobClient.Exists())
//			continue;
//
//		var props = bucketBlobClient.GetProperties().Value;
//
//		if (props.AccessTier == AccessTier.Archive)
//		{
//			if (props.AccessTierChangedOn <= DateTime.Now.AddDays(-5))
//			{
//				bucketBlobClient.SetAccessTier(AccessTier.Cool);
//				totalSize += props.ContentLength;
//				blobName.Dump();
//			}
//		}
//	}
//}
//
//totalSize.Dump();