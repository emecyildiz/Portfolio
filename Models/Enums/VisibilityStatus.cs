namespace Portfolio.Models.Enums
{
    public enum VisibilityStatus
    {

        Draft   = 0,  // not listed anywhere, URL returns 404
        Private = 1,  // Only see in admin panel (for the notes)
        Public  = 2   

    }
}
