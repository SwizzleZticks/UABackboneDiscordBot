using CsvHelper;
using System.Globalization;
using UABackoneBot.Maps;
using UABackoneBot.Models;

namespace UABackoneBot.Services
{
    public class CsvConverterService
    {
        private readonly HttpClient _httpClient = new HttpClient()
        {
            BaseAddress = new Uri("") //add asp.net backend endpoint
        };
        private List<JobInfo>? _jobs;
        public List<JobInfo>? Jobs 
        { 
            get { return _jobs; }
            set { _jobs = value; }
        }
        public List<JobInfo> GetJobs(string filePath)
        {
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Context.RegisterClassMap<JobMap>();
                Jobs = csv.GetRecords<JobInfo>().ToList();
            }

            return Jobs;
        }
    }
}
