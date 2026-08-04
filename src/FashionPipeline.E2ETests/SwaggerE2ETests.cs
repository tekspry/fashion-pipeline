using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Threading.Tasks;

namespace FashionPipeline.E2ETests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class SwaggerE2ETests : PageTest
{
    [Test]
    public async Task Can_Submit_Accessory_Via_Swagger()
    {
        // Navigate to the Swagger UI of the running API (assuming port 5000)
        await Page.GotoAsync("http://localhost:5000/swagger");

        // Assert title
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Swagger UI"));

        // Open the POST endpoint
        await Page.Locator("div[id^='operations-default-post_api_v1_accessory_process']").ClickAsync();

        // Click Try it out
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Try it out" }).ClickAsync();

        // Fill parameters
        await Page.Locator("input[placeholder='name']").FillAsync("Gold Lace");
        await Page.Locator("input[placeholder='category']").FillAsync("Lace");
        await Page.Locator("input[placeholder='imageUrl']").FillAsync("https://example.com/lace.jpg");
        await Page.Locator("input[placeholder='tenantId']").FillAsync("12345678-1234-1234-1234-123456789012");

        // Execute
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Execute" }).ClickAsync();

        // Verify response is 202 Accepted
        var responseCode = Page.Locator(".response-col_status").First;
        await Expect(responseCode).ToContainTextAsync("202");
    }
}