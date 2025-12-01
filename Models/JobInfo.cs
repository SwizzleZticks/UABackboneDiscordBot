using CsvHelper.Configuration.Attributes;

namespace UABackoneBot.Models
{
    public class JobInfo
    {
        [Name("State/Province")]
        public string? Location { get; set; }
        [Name("Trade")]
        public string? Trade { get; set; }
        [Name("Wages")]
        public string? Wages { get; set; }
        [Name("National Pension")]
        public string? NationalPension { get; set; }
        [Name("Local Pension")]
        public string? LocalPension { get; set; }
        [Name("Health")]
        public string? HealthAndWelfare { get; set; }
        [Name("Overtime")]
        public string? Hours { get; set; }
        [Name("Job Start Date")]
        public string? StartDate { get; set; }
        [Name("Job End Date")]
        public string? EndDate { get; set; }
        [Name("# Needed")]
        public int? AmountNeeded { get; set; }
        public string JobKey =>
        $"{Location}|{Trade}|{Wages}|{Hours}|{StartDate}|{EndDate}"; //used for comparison since no id is provided in csv
    }
}
