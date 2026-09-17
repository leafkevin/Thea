using SkiaSharp;
using System;
using System.IO;
using Topten.RichTextKit;

namespace Thea.Alarm.ImageConverter;

public class AlarmImageConverter : IAlarmImageConverter
{
    public byte[] Create(AlarmRequest request)
    {
        // ==========================================
        // 第一步：先不建画布，只做纯文本排版和测量
        // ==========================================
        var textBlock = new TextBlock();
        var titleStyle = new Style { FontFamily = "sans-serif", FontSize = 20, TextColor = SKColor.Parse("#AA0844"), FontWeight = 700 };
        textBlock.AddText($"{request.Title}\n", titleStyle);
        var labelStyle = new Style { FontFamily = "sans-serif", FontSize = 14, TextColor = SKColor.Parse("#AA0844") };
        var valueStyle = new Style { FontFamily = "sans-serif", FontSize = 14, TextColor = SKColors.Black, FontWeight = 500 };
        var errorStyle = new Style { FontFamily = "sans-serif", FontSize = 14, TextColor = SKColor.Parse("#555555") };
        foreach (var message in request.Content)
        {
            var label = message.Key + "：";
            var myValueStyle = valueStyle;
            if (message.Key == "异常内容")
            {
                label += "\n";
                myValueStyle = errorStyle;
            }
            var value = message.Value;
            if (message.Key == "环  境")
            {
                if (message.Value.Contains("正式"))
                {
                    value = "正式";
                    myValueStyle = new Style { FontFamily = "sans-serif", FontSize = 14, TextColor = SKColor.Parse("#EE0505") };
                }
                else if (message.Value.Contains("预发布"))
                {
                    value = "预发布";
                    myValueStyle = new Style { FontFamily = "sans-serif", FontSize = 14, TextColor = SKColor.Parse("#0083FF") };
                }
                else value = "开发";
            }
            textBlock.AddText(label, labelStyle);
            textBlock.AddText($"{value}\n", myValueStyle);
        }
        // 【关键】告诉排版引擎：文本最大只能这么宽，超出的请自动换行
        textBlock.MaxWidth = 330;

        // ==========================================
        // 第二步：获取排版后的实际文本高度
        // ==========================================
        // 这一步引擎已经在内存里算好了每一行的折行位置
        float actualTextHeight = textBlock.MeasuredHeight;

        // ==========================================
        // 第三步：计算最终图片的动态尺寸
        // ==========================================
        int imageWidth = 400; // 图片固定宽度

        // 动态高度 = 顶部外边距(35) + 实际文本高度 + 底部外边距(35)
        int imageHeight = 35 + (int)Math.Ceiling(actualTextHeight) + 35;

        // 【避坑指南】微信发图有限制，如果报错堆栈有一万行，图片会变成几万像素长。
        // 所以最好加一个“最大高度保护”
        int maxAllowedHeight = 3000; // 限制最大高度为 3000px
        bool isTruncated = false;
        if (imageHeight > maxAllowedHeight)
        {
            imageHeight = maxAllowedHeight;
            isTruncated = true; // 标记一下，说明内容被截断了
            textBlock.MaxHeight = maxAllowedHeight - 70; // 限制文本块的渲染高度
        }

        // ==========================================
        // 第四步：根据计算出的动态高度，真正创建画布和背景
        // ==========================================
        using var bitmap = new SKBitmap(imageWidth, imageHeight);
        using var canvas = new SKCanvas(bitmap);

        // 填充底层大背景色
        canvas.Clear(SKColor.Parse("#f4f7f9"));

        // 绘制白色的圆角卡片背景 (自适应高度)
        var cardRect = new SKRect(15, 15, imageWidth - 15, imageHeight - 15);
        using var cardPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(cardRect, 10, 10, cardPaint);

        // ==========================================
        // 第五步：将排版好的文字画到画布上
        // ==========================================
        textBlock.Paint(canvas, new SKPoint(35, 35));

        // 如果被截断了，可以在最底部画一个半透明的渐变或者文字提示 (可选的高级体验)
        if (isTruncated)
        {
            var warningStyle = new Style { FontFamily = "sans-serif", FontSize = 13, TextColor = SKColors.Red };
            var warningBlock = new TextBlock();
            warningBlock.AddText("... (报错信息过长，已折叠)", warningStyle);
            warningBlock.Paint(canvas, new SKPoint(35, imageHeight - 40));
        }

        // 保存输出
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        return stream.ToArray();
    }
}