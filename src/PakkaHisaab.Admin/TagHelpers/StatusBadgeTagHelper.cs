using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PakkaHisaab.Admin.TagHelpers;

/// <summary>&lt;ph-badge text="Paid" kind="green" /&gt; — a colored status chip.
/// Kind is one of teal/green/red/amber/gray (see .ph-badge-* in site.css).</summary>
[HtmlTargetElement("ph-badge")]
public class StatusBadgeTagHelper : TagHelper
{
    public string Text { get; set; } = "";
    public string Kind { get; set; } = "gray";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"ph-badge ph-badge-{Kind}");
        output.Content.SetContent(Text);
    }
}
