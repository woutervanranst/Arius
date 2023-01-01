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
</Query>

BinaryProperties.Count().Dump();

int j = 0;

long size = 0;
long oneMegabyte = 1024 * 1024;

foreach (var bp in BinaryProperties.Where(bp => bp.ArchivedLength <= oneMegabyte))
{
	var pfegs = PointerFileEntries.Where(pfe => pfe.BinaryHash == bp.BinaryHash).ToArray().GroupBy(pfe => pfe.RelativeName).ToArray();
	
	var allLastVersions = pfegs.Select(pfeg => pfeg.OrderBy(pfe => pfe.VersionUtc).Last());
	
	if (allLastVersions.All(pfe => pfe.IsDeleted == 1))
	{
		
		// Delete the binary
		
		// Delete the BinaryProperty
		BinaryProperties.Remove(bp);
		
		// delete the pointerfileentry
		PointerFileEntries.RemoveRange(pfegs.SelectMany(x => x));
		
		size += bp.ArchivedLength;
		
		j++;

		//if (j > 20000)
		//{
		//	SaveChanges();
		//	j = 0;
		//}
	}
}

SaveChanges();
BinaryProperties.Count().Dump();
size.Dump();