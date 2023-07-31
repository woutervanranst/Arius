<Query Kind="Statements">
  <NuGetReference>Arius.Core</NuGetReference>
  <NuGetReference>Azure.Storage.Blobs</NuGetReference>
  <Namespace>Arius.Core.Services</Namespace>
</Query>

var decPath = @"C:\Users\woute\Downloads\arius-archive-pcbackups-2022-01-11T07-33-28.1782850Z.sqlite";

var passphrase = Util.GetPassword("passphrase");

var encPath = decPath + ".gzip";
await using (var ss = File.OpenRead(decPath))
{
	using var ts = File.OpenWrite(encPath);
	await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
}

encPath.Dump();