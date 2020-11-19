//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Linq.Expressions;
//using System.Threading.Tasks;
//using Arius.CommandLine;

//namespace Arius
//{
//    internal class LocalTempManifestRepository : ILocalRepository
//    {
//        private readonly LocalFileFactory _factory;
//        private readonly DirectoryInfo _localTemp;


//        public LocalTempManifestRepository(ICommandExecutorOptions options,
//            Configuration config,
//            LocalFileFactory factory)
//        {
//            _factory = factory;
//            _localTemp = config.TempDir.CreateSubdirectory("manifests");
//        }


//        public ILocalFile GetById(HashValue id)
//        {
//            throw new NotImplementedException();
//        }

//        public IEnumerable<ILocalFile> GetAll(Expression<Func<ILocalFile, bool>> filter = null)
//        {
//            throw new NotImplementedException();
//        }

//        public void Put(ILocalFile entity)
//        {
//            throw new NotImplementedException();
//        }

//        public void PutAll(IEnumerable<ILocalFile> entities)
//        {
//            throw new NotImplementedException();
//        }
//    }
//}