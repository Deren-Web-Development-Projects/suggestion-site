namespace SuggestionAppLibrary.DataAccess;
public class MongoUserData : IUserData
{
   private readonly IMongoCollection<UserModel> _users;

   public MongoUserData(IDbConnection db)
   {
      _users = db.UserCollection;
   }

   public async Task<List<UserModel>> GetUsersAsync()
   {
      var results = await _users.FindAsync(_ => true);
      return results.ToList();
   }

   public async Task<UserModel> GetUser(string id)
   {
      var results = await _users.FindAsync(u => u.Id == id);
      return results.FirstOrDefault();
   }

   // Object Identifier that Azure Actor Directory B to C gives to the user
   public async Task<UserModel> GetUserFromAuthentication(string objectId)
   {
      var results = await _users.FindAsync(u => u.ObjectIdentifier == objectId);
      return results.FirstOrDefault();
   }

   // Creating a new user
   public Task CreateUser(UserModel user)
   {
      return _users.InsertOneAsync(user);
   }

   // Creates a filter to replace one async
   public Task UpdateUser(UserModel user)
   {
      var filter = Builders<UserModel>.Filter.Eq("Id", user.Id);
      return _users.ReplaceOneAsync(
         filter, user, new ReplaceOptions { IsUpsert = true }
      );
   }
}
