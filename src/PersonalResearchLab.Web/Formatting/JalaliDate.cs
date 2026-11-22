using System.Globalization;

namespace PersonalResearchLab.Web.Formatting;

/// <summary>
/// Jalali (Solar Hijri) date formatting for the UI.
/// <para>
/// Timestamps are stored in UTC everywhere. This type only converts for display, and it always
/// converts through the configured time zone so a stored instant never silently shifts meaning.
/// The conversion is implemented directly rather than through a package so the calendar math is
/// auditable and has no external dependency.
/// </para>
/// </summary>
public static class JalaliDate
{
    private static readonly string[] MonthNames =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    private static readonly string[] WeekdayNames =
    {
        "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"
    };

    /// <summary>Default display time zone. Overridable through configuration.</summary>
    public static TimeZoneInfo DefaultTimeZone { get; set; } = ResolveTehran();

    private static TimeZoneInfo ResolveTehran()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next known identifier.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the next known identifier.
            }
        }

        return TimeZoneInfo.Utc;
    }

    public static (int Year, int Month, int Day) ToJalali(DateTime date)
    {
        var gy = date.Year;
        var gm = date.Month;
        var gd = date.Day;

        var gDaysInMonth = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        var jDaysInMonth = new[] { 31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29 };

        var gy2 = gm > 2 ? gy + 1 : gy;

        var days =
            355666 +
            (365 * gy) +
            ((gy2 + 3) / 4) -
            ((gy2 + 99) / 100) +
            ((gy2 + 399) / 400) +
            gd +
            gDaysInMonth.Take(gm - 1).Sum();

        var jy = -1595 + (33 * (days / 12053));
        days %= 12053;
        jy += 4 * (days / 1461);
        days %= 1461;

        if (days > 365)
        {
            jy += (days - 1) / 365;
            days = (days - 1) % 365;
        }

        var jm = 1;
        var jd = 0;

        for (var i = 0; i < 12; i++)
        {
            if (days < jDaysInMonth[i])
            {
                jm = i + 1;
                jd = (int)days + 1;
                break;
            }

            days -= jDaysInMonth[i];
        }

        return (jy, jm, jd);
    }

    /// <summary>Converts a UTC instant to the display time zone.</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            DefaultTimeZone);

    /// <summary>Full Jalali date, e.g. "۲۱ آبان ۱۴۰۵".</summary>
    public static string FormatDate(DateTime utc)
    {
        var local = ToLocal(utc);
        var (year, month, day) = ToJalali(local);
        return $"{ToPersianDigits(day)} {MonthNames[month - 1]} {ToPersianDigits(year)}";
    }

    /// <summary>Jalali date and 24-hour time, e.g. "۲۱ آبان ۱۴۰۵، ساعت ۱۴:۳۰".</summary>
    public static string FormatDateTime(DateTime utc)
    {
        var local = ToLocal(utc);
        var (year, month, day) = ToJalali(local);
        var time = $"{local.Hour:00}:{local.Minute:00}";
        return $"{ToPersianDigits(day)} {MonthNames[month - 1]} {ToPersianDigits(year)}، ساعت {ToPersianDigits(time)}";
    }

    /// <summary>Short numeric Jalali date, e.g. "۱۴۰۵/۰۸/۲۱".</summary>
    public static string FormatShort(DateTime utc)
    {
        var local = ToLocal(utc);
        var (year, month, day) = ToJalali(local);
        return ToPersianDigits($"{year:0000}/{month:00}/{day:00}");
    }

    /// <summary>Weekday name for a UTC instant.</summary>
    public static string FormatWeekday(DateTime utc)
    {
        var local = ToLocal(utc);
        return WeekdayNames[(int)local.DayOfWeek];
    }

    /// <summary>Relative phrasing in Persian, falling back to an absolute date beyond 30 days.</summary>
    public static string FormatRelative(DateTime utc, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var delta = now - utc;

        if (delta < TimeSpan.Zero)
        {
            return FormatDateTime(utc);
        }

        if (delta.TotalSeconds < 60)
        {
            return "همین حالا";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{ToPersianDigits((int)delta.TotalMinutes)} دقیقه پیش";
        }

        if (delta.TotalHours < 24)
        {
            return $"{ToPersianDigits((int)delta.TotalHours)} ساعت پیش";
        }

        if (delta.TotalDays < 30)
        {
            return $"{ToPersianDigits((int)delta.TotalDays)} روز پیش";
        }

        return FormatDate(utc);
    }

    /// <summary>Converts ASCII digits to Persian-Indic digits for display.</summary>
    public static string ToPersianDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
            {
                chars[i] = (char)('۰' + (chars[i] - '0'));
            }
        }

        return new string(chars);
    }

    public static string ToPersianDigits(int value) =>
        ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    public static string ToPersianDigits(long value) =>
        ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Formats a byte count with Persian units.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "بایت", "کیلوبایت", "مگابایت", "گیگابایت" };
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var rounded = unit == 0 ? size.ToString("0", CultureInfo.InvariantCulture)
                                : size.ToString("0.#", CultureInfo.InvariantCulture);

        return $"{ToPersianDigits(rounded)} {units[unit]}";
    }
}
