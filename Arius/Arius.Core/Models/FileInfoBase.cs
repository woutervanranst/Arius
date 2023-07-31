﻿using Arius.Core.Models;
using System;
using System.IO;

namespace Arius.Core.Services;

internal abstract record FileInfoBase
{
    protected readonly FileInfo fi;

    protected FileInfoBase(FileInfo fi)
    {
        this.fi = fi;
    } 

    public string        FullName  => fi.FullName;
    public string        Name      => fi.Name;
    public bool          Exists    => File.Exists(fi.FullName); // fi.Exists needs a Refresh() to update to the latest state;
    public void          Delete()  => fi.Delete();
    public long          Length    => fi.Length;
    public DirectoryInfo Directory => fi.Directory;
    public DateTime CreationTimeUtc
    {
        get => File.GetCreationTimeUtc(FullName);
        set => File.SetCreationTimeUtc(FullName, value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
    }

    public DateTime LastWriteTimeUtc
    {
        get => File.GetLastWriteTimeUtc(FullName);
        set => File.SetLastWriteTimeUtc(FullName, value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
    }

    public FileStream OpenWrite() => fi.OpenWrite(); // Todo add async 

    public void CopyTo(string destFileName) => fi.CopyTo(destFileName);
}


internal record PointerFileInfo : FileInfoBase
{
    public PointerFileInfo(string fileName) : this(new FileInfo(fileName))
    {
    }
    public PointerFileInfo(FileInfo fi) : base(fi)
    {
        if (!fi.FullName.EndsWith(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("This is not a valid PointerFile");
    }

    public string BinaryFileFullName => GetBinaryFileName(fi.FullName);

    public static string GetBinaryFileName(string pointerFileName) => pointerFileName.TrimEnd(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase);
}


internal record BinaryFileInfo : FileInfoBase
{
    public BinaryFileInfo(string fileName) : this(new FileInfo(fileName))
    {
    }
    public BinaryFileInfo(FileInfo fi) : base(fi)
    {
        if (fi.FullName.EndsWith(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("This seems to be a PointerFile");
    }

    public string PointerFileFullName => GetPointerFileName(fi.FullName);

    public static string GetPointerFileName(string binaryFileName) => $"{binaryFileName}{PointerFile.Extension}";
}