// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Playwright;



var dfsdf = TimeSpan.FromDays(1).TotalMilliseconds;

using var playwright = await Playwright.CreateAsync();
var browser = await playwright.Chromium.LaunchAsync();
var productUrl = "https://detail.1688.com/offer/716199727750.html";
var context = await browser.NewContextAsync();
var headers = new Dictionary<string, string>
{
    { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" },
    { "Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6" },
    { "Cache-Control", "max-age=0" },
    { "Sec-Ch-Ua", "\"Not.A/Brand\";v=\"8\", \"Chromium\";v=\"114\", \"Microsoft Edge\";v=\"114\"" },
    { "Sec-Ch-Ua-Mobile", "?0" },
    { "Sec-Ch-Ua-Platform", "\"Windows\"" },
    { "Sec-Fetch-Dest", "document" },
    { "Sec-Fetch-Mode", "navigate" },
    { "Sec-Fetch-Site", "none" },
    { "Sec-Fetch-User", "?1" },
    { "Upgrade-Insecure-Requests", "1" },
    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36 Edg/114.0.1823.67" },
    { "Cookie", "cna=OVAuHfS0Vn0CAWVdBtLG8T9F; taklid=83acf3eade8a4cce8f011f14282bbde6; _bl_uid=Uglknj0nr0qvIt6y1hURbw08awtv; lid=angkevinyur; ali_apache_track=c_mid=b2b-113154844|c_lid=angkevinyur|c_ms=1; __mwb_logon_id__=angkevinyur; mwb=ng; cookie2=16923106e8250dc334516c2551d7b32b; sgcookie=E100UqGbv93m4jX%2F7NFG61aOJ2QhI6ZJ%2FuLEHd5r7QVKqfP%2F4JWU4M0%2FXGd7%2FOYKqMmO8ZufPK9ImuavB7oY9eCMe%2BIS3sB3%2BumM66mJRAqLKCjPEW1YHDcSeWmad9AM6usyTgYOVL2zEHfo4QwlhflRHA%3D%3D; t=71c8830d150f96b4287ad93f6c110056; _tb_token_=e51e56931578b; uc4=nk4=0%40AJTA%2BsxABNvbTQ9zIdQqEA%3D%3D&id4=0%40UOg2tH0q%2FumZdiBIQe2ncsy4ruo%3D; __cn_logon__=false; _m_h5_tk=3dedc70f6964b3785f380028df2b8a27_1689146505496; _m_h5_tk_enc=642876fad61092cd5078ec0b0a7b46e3; xlly_s=1; JSESSIONID=1EC7652D5FA5B6C84329DC291C5785DE; _csrf_token=1689137163525; tfstk=cL6fB7mZpq0XHDwinEZPNO4sdeOPZcoBfSTccyexSqdoodjfitHeRFdcsVpwX31..; l=fBPRaHFPN6vfHPZLBOfaFurza77OxIRbjuPzaNbMi9fPsujByqXOW1sFLsv6CnGVesLJR3ooemR2BiUv0yCKnxv9-T4xQNDmndhyN3pR.; isg=BOHh_CEZIR7fvI1j0VzXHZCA8K37jlWAJTHoJUO2kugCqgB8i946UgKiCN4sWe24" }
};
var page = await context.NewPageAsync();
await page.SetExtraHTTPHeadersAsync(headers);
await page.GotoAsync(productUrl);
var cookies = await context.CookiesAsync(new string[] { productUrl });
var cookieStrings = string.Join(';', cookies.Select(f => $"{f.Name}={f.Value}"));
var response = await page.ContentAsync();
var elements = await page.QuerySelectorAllAsync("div.detail-gallery-turn-wrapper");
await browser.CloseAsync();
Console.WriteLine("Hello, World!");
