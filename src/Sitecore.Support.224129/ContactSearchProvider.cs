using Sitecore.Analytics.Model;
using Sitecore.CES.DeviceDetection;
using Sitecore.Cintel.Commons;
using Sitecore.Cintel.Reporting.Utility;
using Sitecore.Cintel.Search;
using Sitecore.ContentSearch.Linq.Utilities;
using Sitecore.XConnect;
using Sitecore.XConnect.Client;
using Sitecore.XConnect.Client.Configuration;
using Sitecore.XConnect.Collection.Model;
using Sitecore.XConnect.Collection.Model.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Sitecore.Cintel;

namespace Sitecore.Support.Cintel
{
  public class ContactSearchProvider : IContactSearchProvider
  {
    public byte[] Bookmark
    {
      get;
      set;
    }

    public ResultSet<List<IContactSearchResult>> Find(ContactSearchParameters parameters)
    {
      return Task.Run(async () => await FindAsync(parameters)).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    private async Task<ResultSet<List<IContactSearchResult>>> FindAsync(ContactSearchParameters parameters)
    {
      ResultSet<List<IContactSearchResult>> resultSet = new ResultSet<List<IContactSearchResult>>(parameters.PageNumber, parameters.PageSize);
      XConnectClient client = SitecoreXConnectClientConfiguration.GetClient("xconnect/clientconfig");
      try
      {
        string[] facets = new string[]
        {
                    "EngagementMeasures",
                    "Personal",
                    "Emails"
        };
        List<SearchItem> deviceFilters = parameters.AdditionalParameters.Keys.Contains("SearchDeviceFilters") ? (parameters.AdditionalParameters["SearchDeviceFilters"] as List<SearchItem>) : null;
        Task<IAsyncEntityBatchEnumerator<Contact>> task = this.QueryIndex(client.Contacts, parameters, facets);
        task.Result.MoveNext<IReadOnlyCollection<Contact>>();
        IEnumerable<Contact> arg_ED_0 = task.Result.Current;
        this.Bookmark = task.Result.GetBookmark();
        resultSet.TotalResultCount = task.Result.TotalCount;
        List<IContactSearchResult> list = new List<IContactSearchResult>();
        IEnumerator<Contact> enumerator = arg_ED_0.GetEnumerator();
        try
        {
          while (enumerator.MoveNext())
          {
            Contact current = enumerator.Current;
            EngagementMeasures facet = current.GetFacet<EngagementMeasures>("EngagementMeasures");
            var ipInfo = current.Interactions.Select(i => i.GetFacet<IpInfo>(IpInfo.DefaultFacetKey)).LastOrDefault(f => f != null);
            PersonalInformation facet2 = current.GetFacet<PersonalInformation>("Personal");
            EmailAddressList facet3 = current.GetFacet<EmailAddressList>("Emails");
            Interaction interaction = current.Interactions.FirstOrDefault<Interaction>();
            IEnumerable<Interaction> enumerable = current.Interactions;
            if (enumerable.Count<Interaction>() >= 0)
            {
              enumerable = this.ApplyDeviceFilter(enumerable, deviceFilters);
              interaction = enumerable.FirstOrDefault<Interaction>();
            }
            else
            {
              interaction = null;
            }
            if (interaction != null)
            {
              IContactSearchResult item = this.BuildBaseResult(current, facet2, facet3, facet);
              if (facet != null)
              {
                this.PopulateLatestVisit(current, interaction, ipInfo, ref item);
              }
              list.Add(item);
            }
          }
        }
        finally
        {
          if (enumerator != null)
          {
            enumerator.Dispose();
          }
        }
        resultSet.Data.Dataset.Add("ContactSearchResults", list);
      }
      finally
      {
        if (client != null)
        {
          ((IDisposable)client).Dispose();
        }
      }
      return resultSet;
    }

    private Task<IAsyncEntityBatchEnumerator<Contact>> QueryIndex(IAsyncQueryable<Contact> contacts, ContactSearchParameters parameters, string[] facets)
    {
      int pageSize = parameters.PageSize;
      string text = parameters.Match;
      IAsyncQueryable<Contact> source = null;
      Expression<Func<Contact, bool>> expression = null;
      source = from x in contacts
               where x.InteractionsCache().InteractionCaches.Any<InteractionCacheEntry>()
               select x into c
               orderby c.EngagementMeasures().MostRecentInteractionStartDateTime descending
               select c;
      if (!string.IsNullOrEmpty(text) && !text.Equals("*"))
      {
        expression = ((Contact c) => c.Personal().FirstName == text);
        expression = expression.Or((Contact c) => c.Personal().LastName == text);
        expression = expression.Or((Contact c) => c.Emails().PreferredEmail.SmtpAddress == text);
        source = source.Where(expression);
      }
      if (parameters.PageNumber == 1)
      {
        this.Bookmark = null;
      }
      expression = null;
      List<SearchItem> list = parameters.AdditionalParameters.ContainsKey("SearchChannelFilters") ? (parameters.AdditionalParameters["SearchChannelFilters"] as List<SearchItem>) : null;
      if (list != null && list.Count != 0)
      {
        foreach (SearchItem current in list)
        {
          Guid channelId = Guid.Parse(current.ItemId);
          if (expression == null)
          {
            expression = ((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.ChannelId == channelId));
          }
          else
          {
            expression = expression.Or((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.ChannelId == channelId));
          }
        }
        source = source.Where(expression);
      }
      expression = null;
      List<SearchItem> list2 = parameters.AdditionalParameters.ContainsKey("SearchCampaignFilters") ? (parameters.AdditionalParameters["SearchCampaignFilters"] as List<SearchItem>) : null;
      if (list2 != null && list2.Count != 0)
      {
        foreach (SearchItem current2 in list2)
        {
          Guid campaignDefinitionId = new Guid(current2.ItemId);
          if (expression == null)
          {
            expression = ((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.DefinitionIds.Any((Guid d) => d == campaignDefinitionId)));
          }
          else
          {
            expression = expression.Or((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.DefinitionIds.Any((Guid d) => d == campaignDefinitionId)));
          }
        }
        source = source.Where(expression);
      }
      expression = null;
      List<SearchItem> list3 = parameters.AdditionalParameters.ContainsKey("SearchOutcomeFilters") ? (parameters.AdditionalParameters["SearchOutcomeFilters"] as List<SearchItem>) : null;
      if (list3 != null && list3.Count != 0)
      {
        foreach (SearchItem current3 in list3)
        {
          Guid outcomeDefinitionId = Guid.Parse(current3.ItemId);
          if (expression == null)
          {
            expression = ((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.Outcomes.Any((OutcomeCacheEntry o) => o.DefinitionId == outcomeDefinitionId)));
          }
          else
          {
            expression = expression.Or((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.Outcomes.Any((OutcomeCacheEntry o) => o.DefinitionId == outcomeDefinitionId)));
          }
        }
        source = source.Where(expression);
      }
      expression = null;
      List<SearchItem> list4 = parameters.AdditionalParameters.ContainsKey("SearchGoalFilters") ? (parameters.AdditionalParameters["SearchGoalFilters"] as List<SearchItem>) : null;
      if (list4 != null && list4.Count != 0)
      {
        foreach (SearchItem current4 in list4)
        {
          Guid goalDefinitionId = Guid.Parse(current4.ItemId);
          if (expression == null)
          {
            expression = ((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.Goals.Any((GoalCacheEntry g) => g.DefinitionId == goalDefinitionId)));
          }
          else
          {
            expression = expression.Or((Contact c) => c.InteractionsCache().InteractionCaches.Any((InteractionCacheEntry ic) => ic.Goals.Any((GoalCacheEntry g) => g.DefinitionId == goalDefinitionId)));
          }
        }
        source = source.Where(expression);
      }
      return source.WithExpandOptions(new ContactExpandOptions(facets)
      {
        Interactions = new RelatedInteractionsExpandOptions(new string[]
          {
                    "IpInfo",
                    "ProfileScores",
                    "UserAgentInfo"
          })
        {
          StartDateTime = new DateTime?(parameters.FromDate),
          EndDateTime = new DateTime?(parameters.ToDate),
          Limit = new int?(2147483647)
        }
      }).GetBatchEnumerator(this.Bookmark, pageSize);
    }

    private IContactSearchResult BuildBaseResult(Contact contact, PersonalInformation personalInformation, EmailAddressList emailAddressList, EngagementMeasures enaEngagementMeasures)
    {
      var ident = contact.Identifiers.Any(identifier => identifier.IdentifierType == ContactIdentifierType.Known);
      var identType = ident == true ? ContactIdentificationLevel.Known : ContactIdentificationLevel.Anonymous;
      var contactSearch = new ContactSearchResult
      {
        IdentificationLevel = (int)identType,
        ContactId = contact.Id.GetValueOrDefault(),
        FirstName = personalInformation?.FirstName,
        MiddleName = personalInformation?.MiddleName,
        Surname = personalInformation?.LastName,
        PreferredEmail = emailAddressList?.PreferredEmail?.SmtpAddress,
        JobTitle = personalInformation?.JobTitle,
        Value = (enaEngagementMeasures != null) ? enaEngagementMeasures.TotalValue : 0,
        VisitCount = (enaEngagementMeasures != null) ? enaEngagementMeasures.TotalInteractionCount : 0
      };

      return contactSearch;
    }

    private void PopulateLatestVisit(Contact contact, Interaction interaction, IpInfo ipInfo, ref IContactSearchResult contactSearch)
    {
      contactSearch.LatestVisitId = interaction.Id.GetValueOrDefault();
      contactSearch.LatestVisitStartDateTime = interaction.StartDateTime;
      contactSearch.LatestVisitEndDateTime = interaction.EndDateTime;
      contactSearch.LatestVisitPageViewCount = interaction.Events.OfType<PageViewEvent>().Count<PageViewEvent>();
      contactSearch.LatestVisitValue = interaction.EngagementValue;
      EngagementMeasures engagementMeasures = contact.EngagementMeasures();
      if (engagementMeasures != null)
      {
        contactSearch.ValuePerVisit = Calculator.GetAverageValue((double)engagementMeasures.TotalValue, (double)engagementMeasures.TotalInteractionCount);
      }
      if (ipInfo != null)
      {
        contactSearch.LatestVisitLocationCityDisplayName = ipInfo.City;
        contactSearch.LatestVisitLocationCountryDisplayName = ipInfo.Country;
        contactSearch.LatestVisitLocationRegionDisplayName = ipInfo.Region;
        contactSearch.LatestVisitLocationId = null;
      }
    }

    private IEnumerable<Interaction> ApplyDeviceFilter(IEnumerable<Interaction> filteredInteractions, List<SearchItem> deviceFilters)
    {
      if (deviceFilters != null && deviceFilters.Count != 0)
        filteredInteractions = filteredInteractions.Where(i => deviceFilters.Select(d => d.ItemId).Contains(GetDevicetype(i.UserAgent)));

      return filteredInteractions;
    }

    public string GetDevicetype(string userAgent)
    {
      string result = "Unknown";
      if (DeviceDetectionManager.IsEnabled && DeviceDetectionManager.IsReady)
      {
        result = DeviceDetectionManager.GetDeviceInformation(userAgent).DeviceType.ToString();
      }
      else if (DeviceDetectionManager.IsEnabled && !DeviceDetectionManager.IsReady)
      {
        int num = 0;
        TimeSpan timeout = new TimeSpan(3000L);
        while (DeviceDetectionManager.IsReady)
        {
          if (DeviceDetectionManager.IsReady)
          {
            result = DeviceDetectionManager.GetDeviceInformation(userAgent).DeviceType.ToString();
            break;
          }
          if (num == 4)
          {
            break;
          }
          DeviceDetectionManager.CheckInitialization(timeout);
          num++;
        }
      }
      return result;
    }
  }
}
