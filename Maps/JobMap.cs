using CsvHelper.Configuration;
using UABackoneBot.Models;

namespace UABackoneBot.Maps
{
    public class JobMap : ClassMap<JobInfo>
    {
        public JobMap()
        {
            Map(m => m.Location).Name("State/Province");
            Map(m => m.Trade).Name("Trade");
            Map(m => m.Wages).Name("Wages");
            Map(m => m.NationalPension).Name("National Pension");
            Map(m => m.LocalPension).Name("Local Pension");
            Map(m => m.HealthAndWelfare).Name("Health");
            Map(m => m.Hours).Name("Overtime");
            Map(m => m.StartDate).Name("Job Start Date");
            Map(m => m.EndDate).Name("Job End Date");
            Map(m => m.AmountNeeded).Name("# Needed");
        }
    }
}
