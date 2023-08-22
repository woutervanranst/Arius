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


        /// <summary>
        /// Get the size (in KB) of this item
        /// </summary>
        public string Size
        {
            get
            {
                    //Remote
                if (BinaryFile is not null)
                    return BinaryFile.Length.GetBytesReadable(LongExtensions.Size.KB);
                else if (PointerFileEntry is not null)
                    return "Unknown";

                    // Local
                else if (PointerFile is not null)
                    return "Unknown";
                else
                    throw new NotImplementedException();
            }
        }


        /// <summary>
        /// Get the string representing the state of the item for the bollekes (eg. pointer present, blob present, ...)
        /// </summary>
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


        public bool IsDeleted
        {
            get
            {
                return PointerFileEntry.IsDeleted;
            }
        }
    }
}
