namespace Demlo.Application.Common;

public static class PhoneUtil
{
    public static string NormalizePhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

        // Strip spaces and dashes
        phone = phone.Replace(" ", "").Replace("-", "");

        // If it starts with 0, replace with +234
        if (phone.StartsWith("0"))
        {
            return "+234" + phone.Substring(1);
        }

        // If it starts with 234 (but no +), add the +
        if (phone.StartsWith("234") && !phone.StartsWith("+"))
        {
            return "+" + phone;
        }

        return phone;
    }
}