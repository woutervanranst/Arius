//using Azure.Storage.Blobs;
//using Microsoft.Azure.Cosmos.Table;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Arius.Repositories
//{
//    public class AzureStorageAccount
//    {
//        //public static GetAzureRepositoryContainerNames()

//        public AzureStorageAccount(string accountName, string accountKey)
//        {
//            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

//            blobServiceClient = new BlobServiceClient(connectionString);

//            var csa = CloudStorageAccount.Parse(connectionString);
//            tableClient = csa.CreateCloudTableClient();
//        }

//        private readonly BlobServiceClient blobServiceClient;
//        private readonly CloudTableClient tableClient;


//        public IEnumerable<string> GetAzureRepositoryNames()
//        {
//            var tables = tableClient.ListTables().Select(ct => ct.Name).ToArray();

//            var r = blobServiceClient.GetBlobContainers()
//                .Where(bci => tables.Contains($"{bci.Name}{AzureRepository.TableNameSuffix}"))
//                .Select(bci => bci.Name)
//                .ToArray();

//            return r;
//        }

//    }
//}
