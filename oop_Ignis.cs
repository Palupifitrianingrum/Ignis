using System;
using System.Reflection.Metadata;
using System.Security.Principal;

namespace wowo
{

    enum Role
    {
        User,
        Admin
    }
    class AuthenticationService
    {
        public User Authenticate(string email, string password)
        {
            return null;
        }
        public User Register(string name, string email, string password)
        {
            return null;
        }
        public void Logout()
        {
        }
    }
    class User
    {
        private int userId;
        private string name;
        private string email;
        private string passwordHash;
        private Role role;
        private DateTime createdAt;

        public bool Login()
        {
            return 0;
        }
        public void Logout()
        {  
        }
    }

    class Admin : User
    {
        public Hotspot AddManualHotspot()
        {
            return null;
        }

        public void UpdateHotspot()
        {
        }

        public void DeleteHotspot()
        {
        }
    }

    class RegisteredUser : User
    {
        public void AddMonitoringArea()
        {
        }

        public void RemoveMonitoringArea()
        {
        }
    }

    class Notification
    {
        private string notificationId;
        private string title;
        private string message;
        private DateTime createdAt;
        private bool isRead;
        private string hotspotId;

        public void Send()
        {
        }
        public void MarkAsRead()
        {
        }
    }
}