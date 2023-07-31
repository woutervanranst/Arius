<Query Kind="Statements">
  <Namespace>System.IO.Compression</Namespace>
</Query>

var zeroes = new ReadOnlySpan<byte>(Enumerable.Repeat<byte>(0, 1024).ToArray());
var kilobytes = 1024 * 50;

var fn = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "zeroes.txt");
using var fs = File.OpenWrite(fn);
for (int i = 0; i < kilobytes; i++)
{
	fs.Write(zeroes);
}
fs.Close();

using var fs2 = File.OpenRead(fn);
using var fs3 = File.OpenWrite(fn + ".gzip");
using var gzs = new GZipStream(fs3, CompressionLevel.Optimal);
fs2.CopyTo(gzs);