using System;
using System.Collections.Generic;
using System.Text;

namespace Ignis.Frontend.Models
{
    public class UserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
