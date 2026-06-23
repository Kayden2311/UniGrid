using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace unigrid.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                var host = smtpSettings["Host"];
                var portStr = smtpSettings["Port"];
                var username = smtpSettings["Username"];
                var password = smtpSettings["Password"];
                var fromName = smtpSettings["FromName"] ?? "UniGrid Notifications";
                var fromEmail = smtpSettings["FromEmail"] ?? "no-reply@unigrid.com";

                // If not configured, write to mock file or log
                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
                {
                    _logger.LogWarning("[MOCK EMAIL] To: {To}, Subject: {Subject}\nBody: {Body}", toEmail, subject, body);
                    
                    // Write to mock email folder in dev environment
                    var mockEmailDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "mock-emails");
                    if (!Directory.Exists(mockEmailDir))
                    {
                        Directory.CreateDirectory(mockEmailDir);
                    }
                    var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.html";
                    var filePath = Path.Combine(mockEmailDir, fileName);
                    var htmlContent = $@"
<html>
<head><title>{subject}</title></head>
<body style='font-family: sans-serif; background-color: #f8fafc; padding: 20px;'>
    <div style='max-width: 600px; margin: 0 auto; background-color: white; border: 1px solid #e2e8f0; border-radius: 12px; padding: 24px; box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1);'>
        <div style='border-bottom: 1px solid #e2e8f0; padding-bottom: 12px; margin-bottom: 16px;'>
            <span style='font-size: 11px; font-weight: bold; color: #64748b; text-transform: uppercase; letter-spacing: 0.05em;'>Dev Mock Email Delivery</span><br/>
            <strong style='font-size: 14px; color: #0f172a;'>To:</strong> <span style='font-size: 14px; color: #334155;'>{toEmail}</span><br/>
            <strong style='font-size: 14px; color: #0f172a;'>Subject:</strong> <span style='font-size: 14px; color: #334155;'>{subject}</span>
        </div>
        <div>{body}</div>
    </div>
</body>
</html>";
                    await File.WriteAllTextAsync(filePath, htmlContent);
                    return;
                }

                int port = int.TryParse(portStr, out var p) ? p : 587;
                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                using var smtpClient = new SmtpClient(host, port)
                {
                    Credentials = new NetworkCredential(username, password),
                    EnableSsl = true
                };

                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("Email sent successfully to {To}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To}", toEmail);
            }
        }
    }
}
