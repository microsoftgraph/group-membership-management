using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Models.CustomAttributes;

namespace Models
{
    [IgnoreLogging]
    public class EmailType
    {
        [Key]
        public int EmailTypeId { get; set; }

        [Required]
        public string EmailTypeName { get; set; }
        public string EmailContentTemplateName { get; set; }
    }
}