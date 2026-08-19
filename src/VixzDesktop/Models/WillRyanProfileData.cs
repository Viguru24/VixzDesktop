using System;
using System.Collections.Generic;
using System.Linq;
using VixzDesktop.Services;

namespace VixzDesktop.Models
{
    public static class WillRyanProfileData
    {
        public static string ProfileName => "Will Ryan";

        public static readonly List<string> DefaultSubscribedChannels = new List<string>
        {
            "The Robotics State",
            "Benny Johnson",
            "Tousi TV",
            "Liberal Hivemind",
            "Tal Oran - TheTraveler",
            "Dr. Steve Turley",
            "Peter H. Diamandis",
            "BestInTESLA",
            "Matthew Berman",
            "Warren Smith - Secret Scholar",
            "Stephen Gardner",
            "Two Bit da Vinci",
            "ClashIQ",
            "AI Revolution",
            "The Rubin Report",
            "The Podcast of the Lotus Eaters",
            "JCristina",
            "Grjngo - Western Movies",
            "Erin Molan",
            "FutureAzA",
            "Ticker Symbol: YOU",
            "Julian Goldie SEO",
            "Jeff Taylor",
            "Sadhguru",
            "Anastasi In Tech",
            "Sabine Hossenfelder",
            "LARRY with Larry Elder",
            "Timcast IRL",
            "Landeur",
            "Promethean Action",
            "AI Samson",
            "GBNews",
            "What Lurks Beneath",
            "Europe: Informed",
            "Manolo Remiddi",
            "OtherBarak",
            "Tesla Jigsaw",
            "AKSTAR ENG",
            "Oriental Pearl",
            "Amala Ekpunobi",
            "Nick Ponte",
            "Solving The Money Problem",
            "Fox News Clips",
            "Logically Answered",
            "Triggernometry",
            "Turning Point USA",
            "News24",
            "Brandon Lehman",
            "Matt Wolfe",
            "Trish Regan",
            "Valuetainment",
            "Nerdy Rodent",
            "Business Basics",
            "Latest Abraham",
            "StephanZA",
            "Better Stack",
            "Pearl",
            "Gaiea Sanskrit",
            "House of El",
            "AI News & Strategy",
            "OkayRickk",
            "Doug In Exile",
            "Jacob H",
            "Professor Nez",
            "ZOE",
            "Doctor Alekseev",
            "iampauljames",
            "WorldofAI",
            "Vince Dao",
            "Luminox Archives",
            "Xiaomanyc 小马在",
            "Zubair Trabzada",
            "UnHerd",
            "Cleo Abram",
            "Alex Ziskind",
            "Tina Huang",
            "FRANCE 24 English",
            "THEE Sama Sama",
            "NERK NEWS",
            "Firstpost",
            "HeelvsBabyface",
            "David Ondrej",
            "Julia McCoy",
            "metricsmule",
            "Disturbed",
            "lusciousgarden",
            "GHL Wizard",
            "Redacted",
            "sakitech",
            "Angela Rose",
            "Chantress Seba",
            "André Duqum",
            "Right Side Broadcasting Network",
            "El Estepario Siberiano",
            "Melle Baby Music",
            "Magnus Midtbø",
            "Lauren Jumps",
            "Tinalei",
            "Parried"
        };

        public static List<string> SubscribedChannels
        {
            get
            {
                if (StorageService.Settings.SubscribedChannels == null || StorageService.Settings.SubscribedChannels.Count == 0)
                {
                    StorageService.Settings.SubscribedChannels = new List<string>(DefaultSubscribedChannels);
                    StorageService.Save();
                }
                return StorageService.Settings.SubscribedChannels;
            }
        }

        public static void AddSubscribedChannel(string name)
        {
            var trimmed = name.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !SubscribedChannels.Any(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                SubscribedChannels.Insert(0, trimmed);
                StorageService.Save();
            }
        }

        public static void RemoveSubscribedChannel(string name)
        {
            SubscribedChannels.RemoveAll(c => c.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            StorageService.Save();
        }
    }
}
