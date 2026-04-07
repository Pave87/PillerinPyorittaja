using MauiBlazorHybrid.Models;

namespace MauiBlazorHybrid.Core;

/// <summary>
/// Static class containing dose calculation logic extracted from ProductService.
/// The only change from the original code is replacing DateTime.Now with a currentTime parameter for testability.
/// </summary>
public static class DoseCalculator
{
    /// <summary>
    /// Calculates the next dose time for a given dosage schedule.
    /// Extracted from ProductService.CalculateNextDoseTime.
    /// </summary>
    public static DateTime CalculateNextDoseTime(DosageSchedule dosage, DateTime? lastTakenTime, DateTime currentTime)
    {
        // Current time to base calculations on
        DateTime now = lastTakenTime ?? currentTime;

        // If there's no time set in the dosage, default to current time
        TimeOnly dosageTimeOfDay = dosage.Time ?? TimeOnly.FromDateTime(now);

        // Start with today's date at the dosage time
        DateTime baseTime = new DateTime(
            now.Year,
            now.Month,
            now.Day,
            dosageTimeOfDay.Hour,
            dosageTimeOfDay.Minute,
            0);

        // If we've never taken this product before or no dosage ID was recorded
        if (lastTakenTime == null)
        {
            // If today's dose time is already past, schedule for next occurrence
            if (baseTime < currentTime)
            {
                if (dosage.Frequency == "Days")
                {
                    // For daily dosage, schedule for tomorrow
                    return baseTime.AddDays(1);
                }
                else if (dosage.Frequency == "Weeks" && dosage.SelectedDays?.Any() == true)
                {
                    // For weekly dosage, find the next selected day
                    return FindNextWeekdayOccurrence(baseTime, dosage.SelectedDays);
                }
            }
            return baseTime;
        }
        else
        {
            // We have a record of when this product was last taken or scheduled
            DateTime lastTaken = lastTakenTime.Value;

            // Calculate the next dose based on frequency and repetition
            if (dosage.Frequency == "Days")
            {
                // Calculate next dose based on repetition (e.g., every X days)
                DateTime nextDose = lastTaken.Date.AddDays(dosage.Repetition);

                // Set the time part from the dosage's scheduled time
                nextDose = new DateTime(
                    nextDose.Year,
                    nextDose.Month,
                    nextDose.Day,
                    dosageTimeOfDay.Hour,
                    dosageTimeOfDay.Minute,
                    0);

                // If the calculated time is in the past, schedule for the next cycle
                if (nextDose < currentTime)
                {
                    int daysToAdd = dosage.Repetition - (int)(currentTime - nextDose).TotalDays % dosage.Repetition;
                    if (daysToAdd == 0 || (currentTime - nextDose).TotalDays % dosage.Repetition == 0)
                    {
                        daysToAdd = dosage.Repetition;
                    }
                    nextDose = currentTime.Date.AddDays(daysToAdd);

                    // Reset time component
                    nextDose = new DateTime(
                        nextDose.Year,
                        nextDose.Month,
                        nextDose.Day,
                        dosageTimeOfDay.Hour,
                        dosageTimeOfDay.Minute,
                        0);
                }
                return nextDose;
            }
            else if (dosage.Frequency == "Weeks" && dosage.SelectedDays?.Any() == true)
            {
                // For weekly dosage, find the next selected day after the last taken date
                DateTime startPoint = lastTaken.AddDays(1);
                DateTime nextWeeklyDose = FindNextWeekdayOccurrence(startPoint, dosage.SelectedDays);

                // Apply repetition (every X weeks)
                int weeksToAdd = (dosage.Repetition - 1) * 7;
                nextWeeklyDose = nextWeeklyDose.AddDays(weeksToAdd);

                // If the calculated time is in the past, find the next occurrence
                if (nextWeeklyDose < currentTime)
                {
                    nextWeeklyDose = FindNextWeekdayOccurrence(currentTime, dosage.SelectedDays);
                }

                // Set the time part from the dosage's scheduled time
                nextWeeklyDose = new DateTime(
                    nextWeeklyDose.Year,
                    nextWeeklyDose.Month,
                    nextWeeklyDose.Day,
                    dosageTimeOfDay.Hour,
                    dosageTimeOfDay.Minute,
                    0);
                return nextWeeklyDose;
            }

            // Default case, schedule for tomorrow at the dosage time
            return baseTime.AddDays(1);
        }
    }

    /// <summary>
    /// Finds the next occurrence of a weekday from the selected days list.
    /// Extracted from ProductService.FindNextWeekdayOccurrence.
    /// </summary>
    public static DateTime FindNextWeekdayOccurrence(DateTime startDate, List<string> selectedDays)
    {
        DateTime result = startDate;
        int daysToAdd = 0;
        bool found = false;

        // Convert the day names to DayOfWeek enum values for easier comparison
        var selectedDaysOfWeek = selectedDays.Select(day => ParseDayOfWeek(day)).ToList();

        // Try each day, up to 7 days forward
        for (int i = 0; i < 7; i++)
        {
            DateTime checkDate = startDate.AddDays(i);
            if (selectedDaysOfWeek.Contains(checkDate.DayOfWeek))
            {
                daysToAdd = i;
                found = true;
                break;
            }
        }

        // If no selected day found in the next 7 days (shouldn't happen with valid data)
        // then just add a week
        if (!found)
        {
            daysToAdd = 7;
        }

        return startDate.AddDays(daysToAdd);
    }

    /// <summary>
    /// Parses a day abbreviation string to DayOfWeek enum.
    /// Extracted from ProductService.ParseDayOfWeek.
    /// </summary>
    public static DayOfWeek ParseDayOfWeek(string day)
    {
        return day switch
        {
            "Mon" => DayOfWeek.Monday,
            "Tue" => DayOfWeek.Tuesday,
            "Wed" => DayOfWeek.Wednesday,
            "Thu" => DayOfWeek.Thursday,
            "Fri" => DayOfWeek.Friday,
            "Sat" => DayOfWeek.Saturday,
            "Sun" => DayOfWeek.Sunday,
            _ => DayOfWeek.Monday // Default case
        };
    }
}
