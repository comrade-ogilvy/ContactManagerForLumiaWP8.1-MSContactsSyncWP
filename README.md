# MSContactsSyncWP

A Windows Phone 8.1 (WP8.1) Silverlight app that synchronizes Microsoft Account contacts (Hotmail / Outlook.com) to the phone's People hub using the Microsoft Graph API.

## Authentication

The app uses **OAuth2 Device Flow** — a standard authentication method designed for devices without a browser. Here is how it works:

1. Tap **Sign in with Microsoft**
2. The app displays a short code and a URL: `https://microsoft.com/devicelogin`
3. Open that URL on any browser — your PC, tablet, or another phone
4. Enter the code shown in the app
5. Sign in with your Microsoft account in the browser
6. The app detects the successful login automatically and is ready to sync

Your credentials are never entered into the app. You sign in through Microsoft's own website in your own browser, exactly as you would for any other Microsoft service.

## Privacy and Security

- The app never sees your Microsoft account password
- All authentication is handled entirely by Microsoft's servers
- The app only receives an access token — a temporary key that allows it to read your contacts
- The token is stored locally on your phone in the app's isolated storage
- No data is sent anywhere except to Microsoft Graph API (`graph.microsoft.com`)
- The app is read-only — it only downloads contacts to the phone, it does not modify your Microsoft account contacts

## Client ID

The app includes a built-in **App Registration Client ID** — an identifier registered with Microsoft Azure that allows the app to request access to the Contacts API on behalf of the user.

**This Client ID is not a secret.** It identifies the application, not the user. Anyone who has it can only use it to authenticate through the standard Microsoft sign-in flow — they cannot access any user's data without that user explicitly signing in and granting permission.

The built-in Client ID is obfuscated in the source code (XOR encoding) simply to avoid it being indexed by code search engines — not for security reasons.

## Using Your Own Client ID

If you prefer to use your own Azure App Registration instead of the built-in one, you can do so:

1. Go to https://portal.azure.com
2. **Azure Active Directory → App registrations → New registration**
3. Name: anything you like
4. Supported account types: **Personal Microsoft accounts only**
5. Redirect URI: leave blank (Device Flow does not need one)
6. After creating, go to **Authentication → Advanced settings → Allow public client flows → Yes**
7. Go to **API permissions → Add → Microsoft Graph → Delegated:**
   - `User.Read`
   - `Contacts.Read`
8. Copy the **Application (client) ID**

In the app, check **"Use custom App Registration ID"** on the Sign in screen and paste your Client ID there.

To embed your own ID in the source code, replace `_clientIdBytes` in `MainPage.xaml.cs`:

```python
# Run this Python one-liner to get the obfuscated bytes for your Client ID
python3 -c "print([hex(ord(c)^42) for c in 'your-client-id-here'])"
```

## How Sync Works

- **First sync** — downloads all contacts from Microsoft and saves them to the People hub
- **Subsequent syncs** — uses the Microsoft Graph **delta query** API, which returns only contacts that changed since the last sync; unchanged contacts are skipped entirely
- **Deleted contacts** — contacts deleted from your Microsoft account are removed from the phone on the next sync

## Field Mapping

| Microsoft Graph   | People Hub field            | Notes                              |
|-------------------|-----------------------------|------------------------------------|
| givenName         | First name                  |                                    |
| middleName        | Middle name (AdditionalName)|                                    |
| surname           | Last name                   |                                    |
| displayName       | Display name                | Used when first/last name are both empty |
| companyName       | Company                     |                                    |
| jobTitle          | Job title                   |                                    |
| department        | Office location             | No dedicated department field on WP8.1 |
| personalNotes     | Notes                       |                                    |
| nickName          | Nickname                    |                                    |
| mobilePhone       | Mobile telephone            |                                    |
| businessPhones[0] | Work telephone              |                                    |
| homePhones[0]     | Telephone (home)            |                                    |
| emailAddresses[0] | Email                       |                                    |
| emailAddresses[1] | Work email                  |                                    |
| emailAddresses[2] | Other email                 |                                    |
| businessAddress   | Work address                |                                    |
| homeAddress       | Address (home)              | No dedicated home address field on WP8.1 |
| otherAddress      | Other address               |                                    |
| birthday          | Birthdate                   |                                    |

## Building

- Visual Studio 2015
- Target: Windows Phone 8.1 Silverlight (ARM)
- Build Release|ARM and deploy directly to device

## License

Open source. Use freely. Contributions welcome.
