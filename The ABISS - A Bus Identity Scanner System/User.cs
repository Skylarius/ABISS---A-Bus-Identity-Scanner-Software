using System;
using Windows.UI.Xaml.Controls;

namespace The_ABISS___A_Bus_Identity_Scanner_System
{
    class User : IEquatable<User>
    {
        public string uniqueId { get; set; }
        ///<summary>
        ///    Face Identificator obtained by API (https://westcentralus.api.cognitive.microsoft.com/face/v1.0/)
        ///</summary>
        public String FaceId { get; set; }
        public DateTime expirationDate { get; set; }
        ///<summary>
        ///    a 100x100 photo of User's face
        ///</summary>
        public Image PhotoFace { get; set; }

        public enum State {OK, SUSPECTED}

        public State state;
        public User()
        {
            uniqueId = "NA";
            state = State.OK;
        }

        public User(String uniqueId) : this()
        {
            this.uniqueId = uniqueId;
            state = State.OK;
        }

        //Overriding Equals for Contains function
        public bool Equals(User u)
        {
            return (u.uniqueId == this.uniqueId);
        }

    }
}
