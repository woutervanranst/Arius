using Arius.Extensions;
using Arius.Facade;
using Arius.Models;
using System;
using System.Text;

namespace Arius.UI.ViewModels
{
    internal class ItemViewModel : ViewModelBase
    {

        public string ContentName { get; init; }
        public PointerFile PointerFile
        {
            get => pointerFile;
            set
            {
                pointerFile = value;

                OnPropertyChanged(nameof(PointerFile));
                OnPropertyChanged(nameof(ItemState));
            }
        }
        private PointerFile pointerFile;

        public BinaryFile BinaryFile
        {
            get => binaryFile;
            set
            {
                binaryFile = value;

                OnPropertyChanged(nameof(BinaryFile));
                OnPropertyChanged(nameof(ItemState));
            }
        }
        private BinaryFile binaryFile;

        public PointerFileEntryAriusEntry PointerFileEntry
        {
            get => pointerFileEntry;
            set
            {
                pointerFileEntry = value;

                OnPropertyChanged(nameof(PointerFileEntry));
                OnPropertyChanged(nameof(ItemState));
            }
        }
        private PointerFileEntryAriusEntry pointerFileEntry;

        public object Manifest => "";


        public string Size
        {
            get
            {
                if (BinaryFile is not null)
                    return BinaryFile.Length.GetBytesReadable(LongExtensions.Size.KB);
                else if (PointerFileEntry is not null)
                    return "TODO";
                else if (PointerFile is not null)
                    return "Unknown";
                else
                    throw new NotImplementedException();
            }
        }

        public string ItemState
        {
            get
            {
                var itemState = new StringBuilder();

                itemState.Append(BinaryFile is not null ? 'Y' : 'N');
                itemState.Append(PointerFile is not null ? 'Y' : 'N');
                itemState.Append(PointerFileEntry is not null ? 'Y' : 'N');
                itemState.Append(Manifest is not null ? 'A' : throw new NotImplementedException());

                return itemState.ToString();
            }
        }
    }
}
