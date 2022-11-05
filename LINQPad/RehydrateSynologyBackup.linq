<Query Kind="Statements">
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <NuGetReference>CsvHelper</NuGetReference>
  <Namespace>Azure.Storage.Blobs</Namespace>
  <Namespace>CsvHelper</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>CsvHelper.Configuration</Namespace>
  <Namespace>Azure.Storage.Blobs.Models</Namespace>
  <Namespace>Azure</Namespace>
</Query>

var accountName = "vanranst";
var accountKey = "T4ctVN5uZpuqBPvTLqYH+tPInz/rT3BDrBrdmlAYnXnip8BedNkxZxYv13kCBqeu1FERjUqRqGSNEoLyg2iE+A==";
var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

var logContainerName = "$logs";
var logContainer = new BlobContainerClient(connectionString, logContainerName);

var bucketContainerName = "synologybackup";
var bucketContainer = new BlobContainerClient(connectionString, bucketContainerName);

var logs = logContainer.GetBlobs(prefix: $"blob/2022/05/05/").Select(l => l.Name).ToArray();

var totalSize = 0L;

foreach (var logName in logs)
{
	logName.Dump();
	
	var logBlobClient = logContainer.GetBlobClient(logName);
	using var stream = logBlobClient.OpenRead();
	using var reader = new StreamReader(stream);
	var lines = reader.ReadToEnd().Split('\n').Select(l => l.Split(';'));
	
	lines = lines
		.Where(l => l.Count() >=2 && l[2] == "GetBlobProperties");
	var blobs = lines
		.Where(l => l.Count() >= 11 && l[11]
		.StartsWith("\"" + "https://vanranst.blob.core.windows.net:443/synologybackup/synology_1.hbk/Pool"))
		.Select(l => l[11])
		.Where(l => l.Contains("bucket"))
		.Select(l => l.Replace("https://vanranst.blob.core.windows.net:443/synologybackup/", "").TrimStart('"').TrimEnd('"'));
	
	foreach (var blobName in blobs)
	{
		var bucketBlobClient = bucketContainer.GetBlobClient(blobName);
		
		if (!bucketBlobClient.Exists())
			continue;
			
		var props = bucketBlobClient.GetProperties().Value;
		
		if (props.AccessTier == AccessTier.Archive)
		{
			if (props.AccessTierChangedOn <= DateTime.Now.AddDays(-5))
			{
				bucketBlobClient.SetAccessTier(AccessTier.Cool);
				totalSize += props.ContentLength;
				blobName.Dump();
			}
		}
	}
}

totalSize.Dump();	