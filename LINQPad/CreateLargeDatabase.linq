<Query Kind="Statements">
  <Connection>
    <ID>19048be4-c426-4c40-81f2-cab438539c84</ID>
    <NamingServiceVersion>2</NamingServiceVersion>
    <Persist>true</Persist>
    <Driver Assembly="(internal)" PublicKeyToken="no-strong-name">LINQPad.Drivers.EFCore.DynamicDriver</Driver>
    <AttachFileName>C:\Users\woute\Downloads\arius-archive-pcbackups-2022-01-11T07-33-28.1782850Z.sqlite</AttachFileName>
    <DriverData>
      <PreserveNumeric1>True</PreserveNumeric1>
      <EFProvider>Microsoft.EntityFrameworkCore.Sqlite</EFProvider>
    </DriverData>
  </Connection>
  <Namespace>LINQPad.Extensibility.DataContext.DbSchema</Namespace>
</Query>

long.MaxValue.Dump();
long archiveSizeBytes = 1099511627776; //1 * 1024L * 1024L * 1024L * 1024L; // 1 TB
archiveSizeBytes.Dump();
long chunkSize = 32 * 1024; // 32 KB
long numberOfChunks = archiveSizeBytes / chunkSize;

var remainingChunks = numberOfChunks - BinaryProperties.Count();

int j = 0;

for (int i = 0; i < remainingChunks; i++)
{
	var bp = new BinaryProperties { BinaryHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLower() };
	this.BinaryProperties.Add(bp);
	j++;
	
	if (j > 10000)
		SaveChanges();
}

SaveChanges();