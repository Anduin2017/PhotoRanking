using System.ComponentModel.DataAnnotations;

namespace Anduin.PhotoRanking.Models.ManageViewModels;

public class SwitchThemeViewModel
{
    [Required]
    public required string Theme { get; set; }
}
