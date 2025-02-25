using System.ComponentModel.DataAnnotations;

namespace Service;

public class ParserSettings
{
    public string ApiKey { get; set; }
    [Required]
    public string ParserLogin{get;set;}
    [Required] //todo:Remowe this after debug
    public string ParserPassword{get;set;}
}