using LiteDB;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Bot
{
    public class DataStore : DbContext
    {
        public DataStore()
        {
            Database.EnsureCreated();
        }
        public DbSet<Member> members { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=database.db");
    }

    public class Member
    {
        [Key]
        public ulong discordId { get; set; }
        public string amongUsName { get; set; }
    }
}