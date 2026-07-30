namespace RoyalDecisions.Domain
{
    /// <summary>
    /// How seriously a content problem should be treated.
    /// </summary>
    public enum ContentIssueSeverity
    {
        /// <summary>Worth reporting, but content generation may proceed.</summary>
        Warning = 0,

        /// <summary>Content is unusable; generation must abort before writing anything.</summary>
        Error = 1
    }
}
