using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess;
public class MongoSuggestionData : ISuggestionData
{
   private readonly IDbConnection _db;
   private readonly IUserData _userData;
   private readonly IMemoryCache _cache;
   private readonly IMongoCollection<SuggestionModel> _suggestions;
   private const string CacheName = "SuggestionData";

   public MongoSuggestionData(IDbConnection db, IUserData userData, IMemoryCache cache)
   {
      _db = db;
      _userData = userData;
      _cache = cache;
      _suggestions = db.SuggestionCollection;
   }

   public async Task<List<SuggestionModel>> GetAllSuggestions()
   {
      var output = _cache.Get<List<SuggestionModel>>(CacheName);
      if (output is null)
      {
         var results = await _suggestions.FindAsync(s => s.Archived == false);
         output = results.ToList();
         _cache.Set(CacheName, output, TimeSpan.FromMinutes(1));
      }

      return output;
   }

   public async Task<List<SuggestionModel>> GetAllApprovedSuggestions()
   {
      var output = await GetAllSuggestions();

      // Returns all of the suggestions that are approved for release
      return output.Where(x => x.ApprovedForRelease).ToList();
   }

   public async Task<SuggestionModel> GetSuggestion(string id)
   {
      // Fresh lookup from database for a single result
      var results = await _suggestions.FindAsync(s => s.Id == id);
      return results.FirstOrDefault();
   }

   public async Task<List<SuggestionModel>> GetAllSuggestionsWaitingForApproval()
   {
      var output = await GetAllSuggestions();
      return output.Where(x => x.ApprovedForRelease == false && x.Rejected == false).ToList();
   }

   public async Task UpdateSuggestion(SuggestionModel suggestion)
   {
      await _suggestions.ReplaceOneAsync(s => s.Id == suggestion.Id, suggestion);

      // Destroys the cache when something is updated. Next query will include new info.
      _cache.Remove(CacheName);
   }

   public async Task UpvoteSuggestion(string suggestionId, string userId)
   {
      var client = _db.Client;

      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionsInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         var suggestion = (await suggestionsInTransaction.FindAsync(s => s.Id == suggestionId)).First();

         bool isUpvote = suggestion.UserVotes.Add(userId);
         if (!isUpvote)
         {
            suggestion.UserVotes.Remove(userId);
         }

         await suggestionsInTransaction.ReplaceOneAsync(s => s.Id == suggestionId, suggestion);

         var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUser(suggestion.Author.Id);

         if (isUpvote)
         {
            user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
         }
         else
         {
            var suggestionToRemove = user.VotedOnSuggestions.Where(s => s.Id == suggestionId).First();
            user.VotedOnSuggestions.Remove(new BasicSuggestionModel(suggestion));
         }

         await usersInTransaction.ReplaceOneAsync(u => u.Id == userId, user);
         await session.CommitTransactionAsync();

         _cache.Remove(CacheName);
      }
      catch (Exception ex)
      {
         await session.AbortTransactionAsync();
         throw;
      }
   }

   public async Task CreateSuggestion(SuggestionModel suggestion)
   {
      var client = _db.Client;

      using var session = await client.StartSessionAsync();

      session.StartTransaction();

      try
      {
         var db = client.GetDatabase(_db.DbName);
         var suggestionsInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
         await suggestionsInTransaction.InsertOneAsync(suggestion);

         var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
         var user = await _userData.GetUser(suggestion.Author.Id);
         user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));
         await usersInTransaction.ReplaceOneAsync(u => u.Id == user.Id, user);

         await session.CommitTransactionAsync();

         // Cache is not updated because the user will not see a suggestion until Admin approved
         // Admin won't see new suggestions until time wait (currently 1 minute)
      }
      catch (Exception ex)
      {
         await session.AbortTransactionAsync();
         throw;
      }
   }
}
