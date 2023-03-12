namespace Arius.Core.Commands.Validate;

internal class ValidateCommand
{
        //logger.LogInformation($"Validating {pointerFile.FullName}...");

        // TODO with blobClient.ListAllAsync of we gaan verdrinken in de ExistAsync calls $$$$

        ////    // Validate the manifest
        ////    var chunkHashes = await repo.GetChunkHashesAsync(pointerFile.Hash);

        ////    if (!chunkHashes.Any())
        ////        throw new InvalidOperationException($"Manifest {pointerFile.Hash} (of PointerFile {pointerFile.FullName}) contains no chunks");

        ////    double length = 0;
        ////    foreach (var chunkHash in chunkHashes)
        ////    {
        ////        var cb = repo.GetChunkBlobByHash(chunkHash, false);
        ////        length += cb.Length;
        ////    }

        ////    var bfi = pointerFile.BinaryFileInfo;
        ////    if (bfi.Exists)
        ////    {
        ////        //TODO if we would know the EXACT/uncompressed size from the PointerFileEntry - use that
        ////        if (bfi.Length / length < 0.9)
        ////            throw new InvalidOperationException("something is wrong");
        ////    }
        ////    else
        ////    {
        ////        //TODO if we would know the expected size from the PointerFileEntry - use that
        ////        if (length == 0)
        ////            throw new InvalidOperationException("something is wrong");
        ////    }

        //logger.LogInformation($"Validating {pointerFile.FullName}... OK!");
}