using System.ComponentModel.DataAnnotations;

namespace Arius.Web.Application;

public class StorageAccountViewModel
{
    [Display(AutoGenerateField = false)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Account Name is required.")]
    [Display(Name = "Account Name")]
    public string AccountName { get; set; }

    [Required(ErrorMessage = "Account Key is required.")]
    [Display(Name = "Account Key")]
    public string AccountKey { get; set; }
}