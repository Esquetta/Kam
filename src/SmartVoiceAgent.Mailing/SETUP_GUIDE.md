# Mailing Service Setup Guide

## Provider-Specific Configuration

### Gmail (Google)

#### Using App Password (Recommended)

1. **Enable 2-Factor Authentication** (required for App Passwords)
   - Go to https://myaccount.google.com/security
   - Under "Signing in to Google", click "2-Step Verification"
   - Follow the setup process

2. **Generate App Password**
   - Go to https://myaccount.google.com/apppasswords
   - Select app: "Mail"
   - Select device: "Other (Custom name)" → type "KAM Assistant"
   - Click "Generate"
   - Copy the 16-character password (e.g., `abcd efgh ijkl mnop`)

3. **Configuration**
   ```json
   {
     "Email": {
       "Provider": "Gmail",
       "Username": "your-email@gmail.com",
       "AppPassword": "abcdefghijklmnop",
       "FromAddress": "your-email@gmail.com",
       "FromName": "KAM Assistant"
     }
   }
   ```

#### Using OAuth2

For production applications, OAuth2 is more secure. You'll need:
- Google Cloud Console project
- OAuth2 credentials (Client ID, Client Secret)
- Access token and refresh token

```csharp
services.AddGmailOAuthServices(
    email: "your-email@gmail.com",
    accessToken: "ya29.a0AfH6SMBx...",
    displayName: "KAM Assistant");
```

---

### Outlook / Hotmail / Live

#### Using App Password

1. **Enable Two-step Verification**
   - Go to https://account.microsoft.com/security
   - Click "Advanced security options"
   - Turn on "Two-step verification"

2. **Create App Password**
   - Under "App passwords", click "Create a new app password"
   - Name it "KAM Assistant"
   - Copy the generated password

3. **Configuration**
   ```json
   {
     "Email": {
       "Provider": "Outlook",
       "Username": "your-email@outlook.com",
       "AppPassword": "your-app-password",
       "FromAddress": "your-email@outlook.com",
       "FromName": "KAM Assistant"
     }
   }
   ```

---

### Microsoft 365 / Office 365

Same as Outlook:

```json
{
  "Email": {
    "Provider": "Office365",
    "Username": "your-email@company.com",
    "AppPassword": "your-app-password",
    "FromAddress": "your-email@company.com",
    "FromName": "KAM Assistant"
  }
}
```

---

### Yahoo Mail

#### Using App Password

1. **Enable Two-step Verification**
   - Go to https://login.yahoo.com/account/security
   - Turn on "Two-step verification"

2. **Generate App Password**
   - Click "Generate app password"
   - Select "Other app"
   - Name it "KAM Assistant"
   - Copy the password

3. **Configuration**
   ```json
   {
     "Email": {
       "Provider": "Yahoo",
       "Username": "your-email@yahoo.com",
       "AppPassword": "your-app-password",
       "FromAddress": "your-email@yahoo.com",
       "FromName": "KAM Assistant"
     }
   }
   ```

---

### SendGrid (Recommended for Production)

SendGrid uses API Keys instead of passwords:

1. **Get API Key**
   - Sign up at https://sendgrid.com
   - Go to Settings → API Keys
   - Create API Key with "Mail Send" permissions
   - Copy the key (starts with `SG.`)

2. **Configuration**
   ```json
   {
     "Email": {
       "Provider": "SendGrid",
       "Username": "apikey",
       "Password": "SG.xxxxxxxxxxxxxxxxxxxx",
       "FromAddress": "notifications@yourdomain.com",
       "FromName": "KAM Assistant"
     }
   }
   ```

---

### Mailgun

1. **Get SMTP Credentials**
   - Sign up at https://www.mailgun.com
   - Go to Sending → Domains → Select your domain
   - Find SMTP credentials

2. **Configuration**
   ```json
   {
     "Email": {
       "Provider": "Mailgun",
       "Host": "smtp.mailgun.org",
       "Port": 587,
       "Username": "postmaster@your-domain.com",
       "Password": "your-mailgun-password",
       "FromAddress": "notifications@your-domain.com",
       "FromName": "KAM Assistant"
     }
   }
   ```

---

### Amazon SES

1. **Get SMTP Credentials**
   - Go to AWS Console → SES
   - Click "SMTP settings" → "Create SMTP credentials"
   - Download the credentials

2. **Configuration**
   ```json
   {
     "Email": {
       "Provider": "AmazonSES",
       "Host": "email-smtp.us-east-1.amazonaws.com",
       "Port": 587,
       "Username": "AKIAxxxxxxxxxxxxxxxx",
       "Password": "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
       "FromAddress": "notifications@your-domain.com",
       "FromName": "KAM Assistant"
     }
   }
   ```

---

## Complete appsettings.json Example

```json
{
  "Email": {
    "Provider": "Gmail",
    "Host": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "UseStartTls": true,
    "AuthMethod": "AppPassword",
    "Username": "kam.notifications@gmail.com",
    "AppPassword": "abcdefghijklmnop",
    "FromAddress": "kam.notifications@gmail.com",
    "FromName": "KAM Neural Core",
    
    "Options": {
      "RateLimitPerMinute": 60,
      "MaxAttachmentSizeMb": 25,
      "TrackOpens": false,
      "TrackClicks": false,
      "SandboxMode": false
    }
  }
}
```

---

## Program.cs Registration

### Option 1: Using Configuration
```csharp
services.AddMailingServices(configuration);
```

### Option 2: Using Gmail with App Password
```csharp
services.AddGmailServices(
    email: "your-email@gmail.com",
    appPassword: "abcdefghijklmnop",
    displayName: "KAM Assistant");
```

### Option 3: Using SendGrid
```csharp
services.AddSendGridServices(
    apiKey: "SG.xxxxxxxxxxxxxxxxxxxx",
    fromEmail: "notifications@yourdomain.com",
    fromName: "KAM Assistant");
```

### Option 4: Sandbox Mode (Testing)
```csharp
services.AddMailingServicesSandbox();
```

---

## Troubleshooting

### Gmail: "Username and Password not accepted"
- Make sure you're using an **App Password**, not your regular password
- Ensure 2-Factor Authentication is enabled
- Check that "Less secure app access" is disabled (it's okay, App Passwords work with it disabled)

### Outlook: "Authentication failed"
- Use App Password, not regular password
- Ensure account doesn't have security blocks

### "The SMTP server requires a secure connection"
- Set `EnableSsl: true` and `UseStartTls: true`
- Use port 587 for TLS or 465 for SSL

### Rate Limiting
- Gmail: 500 emails per day (free), 2000 (Workspace)
- Outlook: 300 emails per day
- Use SendGrid/Amazon SES for higher limits

---

## Security Best Practices

1. **Never commit credentials** - Use User Secrets or Environment Variables
   ```bash
   dotnet user-secrets set "Email:AppPassword" "your-password"
   ```

2. **Use App Passwords** instead of regular passwords when possible

3. **Enable 2FA** on your email account

4. **For production**, use dedicated email services like SendGrid or Amazon SES

5. **Rotate credentials** periodically
