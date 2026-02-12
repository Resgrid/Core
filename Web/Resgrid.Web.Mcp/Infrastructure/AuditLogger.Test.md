# AuditLogger Sanitization Test

## Overview
The AuditLogger has been updated to sanitize sensitive data before logging to prevent exposure of credentials and tokens.

## What Was Changed

### 1. Added Sensitive Field Detection
A HashSet of sensitive field names is maintained:
- password
- accessToken
- token
- secret
- apiKey
- authorization
- bearer
- credential
- privateKey

### 2. Implemented Sanitization Logic
The `SanitizeSensitiveData` method:
- Converts objects to JSON for manipulation
- Recursively scans all properties
- Replaces sensitive field values with "***REDACTED***"
- Returns the sanitized object

### 3. Updated LogToolCallAsync
The method now calls `SanitizeSensitiveData(arguments)` before storing in audit entry details.

### 4. Protected AccessToken in AuditEntry
Added `[JsonIgnore]` attribute to the `AccessToken` property to prevent it from being serialized in logs.

## Example Behavior

### Before Sanitization:
```json
{
  "toolName": "authenticate",
  "arguments": {
    "username": "user@example.com",
    "password": "MySecretPassword123"
  }
}
```

### After Sanitization:
```json
{
  "toolName": "authenticate",
  "arguments": {
    "username": "user@example.com",
    "password": "***REDACTED***"
  }
}
```

### Complex Nested Example:
```json
{
  "toolName": "send_message",
  "arguments": {
    "accessToken": "***REDACTED***",
    "message": {
      "subject": "Hello",
      "body": "This is a test",
      "credentials": "***REDACTED***"
    },
    "recipients": ["user1", "user2"]
  }
}
```

## Security Benefits

1. **Prevents Password Leakage**: User passwords in authentication logs are redacted
2. **Protects Access Tokens**: API tokens in tool calls are not exposed
3. **Handles Nested Objects**: Recursively sanitizes nested structures
4. **Fail-Safe**: If sanitization fails, returns a safe placeholder
5. **Case-Insensitive**: Works regardless of field name casing

## Testing

To verify the sanitization is working:

1. Call the authenticate tool with a username/password
2. Check the audit logs
3. Verify password is shown as "***REDACTED***"

4. Call any tool with an accessToken
5. Check the audit logs  
6. Verify accessToken is shown as "***REDACTED***"

