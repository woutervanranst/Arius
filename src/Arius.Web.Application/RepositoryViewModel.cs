using Arius.Core.Domain.Storage;
using System.ComponentModel.DataAnnotations;

namespace Arius.Web.Application;

public class RepositoryViewModel
{
    [Display(AutoGenerateField = false)]
    public int Id { get; set; }

    [Required(ErrorMessage = "Local Path is required.")]
    [Display(Name = "Local Path")]
    public string LocalPath { get; set; }

    [Required(ErrorMessage = "Container Name is required.")]
    [Display(Name = "Container Name")]
    public string ContainerName { get; set; }

    [Required(ErrorMessage = "Passphrase is required.")]
    [Display(Name = "Passphrase")]
    public string Passphrase { get; set; }

    [Required(ErrorMessage = "Tier is required.")]
    [Display(Name = "Tier")]
    public StorageTier Tier { get; set; }

    [Display(Name = "Remove Local")]
    public bool RemoveLocal { get; set; }

    [Display(Name = "Deduplication")]
    public bool Dedup { get; set; }

    [Display(Name = "Fast Hash")]
    public bool FastHash { get; set; }
}