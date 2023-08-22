# References

refer to https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli

# Initial creation on existing db

Refer https://github.com/dotnet/efcore/issues/26870#issuecomment-985364245

dotnet ef migrations add Initial -o Repositories/StateDbMigrations
remove everything from the Up() and Down() methods

# Adding a new migration

dotnet ef migrations add AddBlogCreatedTimestamp

# Generate scripts
# First migration (with manual tweaks to create the EF migrations Table)
# dotnet ef migrations script InitialCreate AddRemovePointerFileExtensionConverter
# rollback
# dotnet ef migrations script AddRemovePointerFileExtensionConverter InitialCreate

# Apply

dotnet ef database update --connection "Data Source=C:\Users\woute\Downloads\arius - Copy.sqlite"