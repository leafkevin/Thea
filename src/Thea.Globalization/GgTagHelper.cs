using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Threading.Tasks;

namespace Thea.Globalization;

[HtmlTargetElement("gr")]
public class GrTagHelper : TagHelper
{
    private readonly IGlobalizationResource globalizationGlossary;

    public GrTagHelper(IGlobalizationResource globalizationGlossary)
    {
        this.globalizationGlossary = globalizationGlossary;
    }
    [HtmlAttributeName("tag")]
    public string Tag { get; set; }
    [HtmlAttributeName("configuration")]
    public string Configuration { get; set; }
    [HtmlAttributeName("language")]
    public string Language { get; set; }
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = string.Empty;
        var tagValue = await this.globalizationGlossary.GetGlossaryAsync(this.Tag, this.Language);
        output.Content.SetHtmlContent(tagValue);
        output.TagMode = TagMode.SelfClosing;
    }
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = string.Empty;
        var tagValue = this.globalizationGlossary.GetGlossary(this.Tag, this.Language);
        output.Content.SetHtmlContent(tagValue);
        output.TagMode = TagMode.SelfClosing;
    }
}
