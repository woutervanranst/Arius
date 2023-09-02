### Restore manually

#### Getting the correct binary

1. Open the `.arius.pointer` file (with Notepad) and look for the `BinaryHash` value.
1. Using Azure Storage Explorer, navigate to the correct container in the storage account and, in the `chunks` folder, locate the blob with the maching name.
`. Decrypt and unpack using the below steps

If the pointer is not available locally:
1. Download the most recent file in the `states` folder in the storage account`
1. Decrypt and unpack using the below steps
1. Using `DB Browser for SQLite`, navigate to the `PointerFileEntries` table, filter on `RelativeName` to find the correct `BinaryHash`
`. Proceed as above

If you cannot locate a chunk with matching `BinaryHash` value or arius was run with the `--dedup` option:

1. In the `chunklist` folder, download the blob with matching value
1. Open the file with Notepad
1. Download, decrypt and unpack each of the chunks as listed in the file
1. Concatenate the chunks (using `cat chunk1 chunk2 chunk3 ... > original.file` (Linux) or `copy chunk1 + chunk2 + chunk3 + ... > original.file` (Windows)) 

#### Decrypt and unpack

Arius files are gzip'ped and then encrypted with AES256. To decrypt:

```
# 1. Decrypt with OpenSSL
openssl enc -d -aes-256-cbc -in $ENCRYPTED_FILE -out original.file.gz -pass pass:$PASSPHRASE -pbkdf2

# 2. Unpack
gzip -d original.file.gz -f

# 3. at this point 'original.file' will be the original binary
```