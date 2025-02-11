﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Reading.BatchedQuerying
{
    public class batched_querying_acceptance_Tests : OneOffConfigurationsContext
    {
        private readonly ITestOutputHelper _output;
        private readonly Target target1 = Target.Random();
        private readonly Target target2 = Target.Random();
        private readonly Target target3 = Target.Random();
        protected User user1 = new User {UserName = "A1", FirstName = "Justin", LastName = "Houston"};
        protected User user2 = new User {UserName = "B1", FirstName = "Tamba", LastName = "Hali"};

        protected AdminUser admin1 = new AdminUser
        {
            UserName = "A2",
            FirstName = "Derrick",
            LastName = "Johnson",
            Region = "Midwest"
        };

        protected AdminUser admin2 = new AdminUser
        {
            UserName = "B2",
            FirstName = "Eric",
            LastName = "Berry",
            Region = "West Coast"
        };

        protected SuperUser super1 = new SuperUser
        {
            UserName = "A3",
            FirstName = "Dontari",
            LastName = "Poe",
            Role = "Expert"
        };

        protected SuperUser super2 = new SuperUser
        {
            UserName = "B3",
            FirstName = "Sean",
            LastName = "Smith",
            Role = "Master"
        };

        public batched_querying_acceptance_Tests(ITestOutputHelper output)
        {
            _output = output;
            DocumentTracking = DocumentTracking.IdentityOnly;
            StoreOptions(_ =>
            {
                _.Schema.For<User>().AddSubClass(typeof (AdminUser)).AddSubClass(typeof (SuperUser))
                    .Duplicate(x => x.FirstName).Duplicate(x => x.LastName);
            });

            theSession.Logger = new TestOutputMartenLogger(_output);
            theSession.Store(target1, target2, target3);
            theSession.Store(user1, user2, admin1, admin2, super1, super2);

            theSession.SaveChanges();
        }



        public void sample_config()
        {
            #region sample_configure-hierarchy-of-types
            var store = DocumentStore.For(_ =>
            {
                _.Connection("connection to your database");

                _.Schema.For<User>()
                    // generic version
                    .AddSubClass<AdminUser>()

                    // By document type object
                    .AddSubClass(typeof (SuperUser));
            });

            using (var session = store.QuerySession())
            {
                // query for all types of User and User itself
                session.Query<User>().ToList();

                // query for only SuperUser
                session.Query<SuperUser>().ToList();
            }
            #endregion
        }

        #region sample_FindByFirstName
        public class FindByFirstName : ICompiledQuery<User, User>
        {
            public string FirstName { get; set; }

            public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
            {
                return q => q.FirstOrDefault(x => x.FirstName == FirstName);
            }
        }
        #endregion

        [Fact]
        public async Task can_query_with_compiled_queries()
        {
            #region sample_batch-query-with-compiled-queries
            var batch = theSession.CreateBatchQuery();

            var justin = batch.Query(new FindByFirstName {FirstName = "Justin"});
            var tamba = batch.Query(new FindByFirstName {FirstName = "Tamba"});

            await batch.Execute();

            (await justin).Id.ShouldBe(user1.Id);
            (await tamba).Id.ShouldBe(user2.Id);
            #endregion
        }

        [Fact]
        public void can_query_synchronously_with_compiled_queries()
        {
            #region sample_batch-query-with-compiled-queries-synchronously
            var batch = theSession.CreateBatchQuery();

            var justin = batch.Query(new FindByFirstName { FirstName = "Justin" });
            var tamba = batch.Query(new FindByFirstName { FirstName = "Tamba" });

            batch.ExecuteSynchronously();

            justin.Result.Id.ShouldBe(user1.Id);
            tamba.Result.Id.ShouldBe(user2.Id);
            #endregion
        }


        public async Task sample_usage_of_compiled_query()
        {
            #region sample_using-compiled-query
            var justin = theSession.Query(new FindByFirstName {FirstName = "Justin"});

            var tamba = await theSession.QueryAsync(new FindByFirstName {FirstName = "Tamba"});
            #endregion
        }

        [Fact]
        public async Task can_query_with_user_supplied_sql()
        {
            var batch = theSession.CreateBatchQuery();

            var justin = batch.Query<User>("where first_name = ?", "Justin");
            var tamba = batch.Query<User>("where first_name = ? and last_name = ?", "Tamba", "Hali");

            await batch.Execute();

            (await justin).Single().Id.ShouldBe(user1.Id);
            (await tamba).Single().Id.ShouldBe(user2.Id);
        }

        [Fact]
        public async Task can_find_the_first_value()
        {
            var batch = theSession.CreateBatchQuery();

            var firstUser = batch.Query<User>().OrderBy(_ => _.FirstName).First();
            var firstAdmin = batch.Query<SuperUser>().OrderBy(_ => _.FirstName).First();

            await batch.Execute();

            (await firstUser).UserName.ShouldBe("A2");
            (await firstAdmin).UserName.ShouldBe("A3");
        }

        [Fact]
        public async Task can_find_the_first_or_default_value()
        {
            var batch = theSession.CreateBatchQuery();

            var firstUser = batch.Query<User>().OrderBy(_ => _.FirstName).FirstOrDefault();
            var firstAdmin = batch.Query<SuperUser>().OrderBy(_ => _.FirstName).FirstOrDefault();
            var noneUser = batch.Query<User>().FirstOrDefault(_ => _.FirstName == "not me");

            await batch.Execute();

            (await firstUser).UserName.ShouldBe("A2");
            (await firstAdmin).UserName.ShouldBe("A3");
            SpecificationExtensions.ShouldBeNull((await noneUser));
        }

        [Fact]
        public async Task single_and_single_or_default()
        {
            var batch = theSession.CreateBatchQuery();

            var tamba = batch.Query<User>().Where(_ => _.FirstName == "Tamba").Single();
            var justin = batch.Query<User>().Where(_ => _.FirstName == "Justin").SingleOrDefault();

            var noneUser = batch.Query<User>().SingleOrDefault(_ => _.FirstName == "not me");

            await batch.Execute();

            (await tamba).FirstName.ShouldBe("Tamba");
            (await justin).FirstName.ShouldBe("Justin");
            SpecificationExtensions.ShouldBeNull((await noneUser));
        }


        [Fact]
        public async Task can_query_documents()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.Query<User>().ToList();
            var anyAdmins = batch.Query<AdminUser>().ToList();
            var anyIntDocs = batch.Query<IntDoc>().ToList();
            var aUsers = batch.Query<User>().Where(_ => _.UserName.StartsWith("A")).ToList();
            var cUsers = batch.Query<User>().Where(_ => _.UserName.StartsWith("C")).ToList();

            await batch.Execute();

            (await anyUsers).OrderBy(x => x.FirstName).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, admin2.Id, user1.Id, super2.Id, user2.Id);


            (await anyAdmins).OrderBy(x => x.FirstName)
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, admin2.Id);
            (await anyIntDocs).Any().ShouldBeFalse();
            (await aUsers).OrderBy(x => x.FirstName)
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, user1.Id);
            (await cUsers).Any().ShouldBeFalse();
        }

        [Fact]
        public void can_query_documents_synchronously()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.Query<User>().ToList();
            var anyAdmins = batch.Query<AdminUser>().ToList();
            var anyIntDocs = batch.Query<IntDoc>().ToList();
            var aUsers = batch.Query<User>().Where(_ => _.UserName.StartsWith("A")).ToList();
            var cUsers = batch.Query<User>().Where(_ => _.UserName.StartsWith("C")).ToList();

            batch.Execute();

            anyUsers.Result.OrderBy(x => x.FirstName).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, admin2.Id, user1.Id, super2.Id, user2.Id);


            anyAdmins.Result.OrderBy(x => x.FirstName)
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, admin2.Id);
            anyIntDocs.Result.Any().ShouldBeFalse();
            aUsers.Result.OrderBy(x => x.FirstName)
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, user1.Id);
            cUsers.Result.Any().ShouldBeFalse();
        }


        [Fact]
        public async Task can_query_for_any()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.Query<User>().Any();
            var anyAdmins = batch.Query<AdminUser>().Any();
            var anyIntDocs = batch.Query<IntDoc>().Any();
            var aUsers = batch.Query<User>().Any(_ => _.UserName.StartsWith("A"));
            var cUsers = batch.Query<User>().Any(_ => _.UserName.StartsWith("C"));

            await batch.Execute();

            (await anyUsers).ShouldBeTrue();
            (await anyAdmins).ShouldBeTrue();
            (await anyIntDocs).ShouldBeFalse();
            (await aUsers).ShouldBeTrue();
            (await cUsers).ShouldBeFalse();
        }

        [Fact]
        public async Task can_query_for_count()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.Query<User>().Count();
            var anyAdmins = batch.Query<AdminUser>().Count();
            var anyIntDocs = batch.Query<IntDoc>().Count();
            var aUsers = batch.Query<User>().Count(_ => _.UserName.StartsWith("A"));
            var cUsers = batch.Query<User>().Count(_ => _.UserName.StartsWith("C"));

            await batch.Execute();

            (await anyUsers).ShouldBe(6);
            (await anyAdmins).ShouldBe(2);
            (await anyIntDocs).ShouldBe(0);
            (await aUsers).ShouldBe(3);
            (await cUsers).ShouldBe(0);
        }

        [Fact]
        public async Task can_find_one_doc_at_a_time_that_is_not_in_identity_map()
        {
            var batch = theSession.CreateBatchQuery();
            var task1 = batch.Load<Target>(target1.Id);
            var task3 = batch.Load<Target>(target3.Id);

            await batch.Execute();

            SpecificationExtensions.ShouldNotBeNull((await task1).ShouldBeOfType<Target>());
            SpecificationExtensions.ShouldNotBeNull((await task3).ShouldBeOfType<Target>());
        }

        [Fact]
        public async Task can_find_docs_by_id_that_should_be_in_identity_map()
        {
            DocumentTracking = DocumentTracking.IdentityOnly;

            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.Load<Target>(target1.Id);
            var task3 = batch1.Load<Target>(target3.Id);

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task21 = batch2.Load<Target>(target1.Id);
            var task23 = batch2.Load<Target>(target3.Id);

            await batch2.Execute();

            (await task1).ShouldBeSameAs(await task21);
            (await task3).ShouldBeSameAs(await task23);
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id()
        {
            theSession.Logger = new TestOutputMartenLogger(_output);
            var batch1 = theSession.CreateBatchQuery();
            var task = batch1.LoadMany<Target>().ById(target1.Id, target3.Id);

            await batch1.Execute();

            var list = await task;

            list.Count().ShouldBe(2);
            list.Any(x => x.Id == target1.Id).ShouldBeTrue();
            list.Any(x => x.Id == target3.Id).ShouldBeTrue();
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id_with_identity_map()
        {
            DocumentTracking = DocumentTracking.IdentityOnly;

            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.LoadMany<Target>().ById(target1.Id, target3.Id);

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task2 = batch2.LoadMany<Target>().ById(target1.Id, target3.Id);

            await batch2.Execute();

            (await task1).ShouldHaveTheSameElementsAs(await task2);
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id_2()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task = batch1.LoadMany<Target>().ByIdList(new List<Guid> {target1.Id, target3.Id});

            await batch1.Execute();

            var list = await task;

            list.Count().ShouldBe(2);
            list.Any(x => x.Id == target1.Id).ShouldBeTrue();
            list.Any(x => x.Id == target3.Id).ShouldBeTrue();
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id_with_identity_map_2()
        {


            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.LoadMany<Target>().ByIdList(new List<Guid> {target1.Id, target3.Id});

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task2 = batch2.LoadMany<Target>().ByIdList(new List<Guid> {target1.Id, target3.Id});

            await batch2.Execute();

            (await task1).ShouldHaveTheSameElementsAs(await task2);
        }


        [Fact]
        public async Task can_use_select_transformations_to_single_field_in_batch()
        {
            var batch1 = theSession.CreateBatchQuery();

            var toList = batch1.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName).ToList();
            var first = batch1.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName).First();
            var firstOrDefault =
                batch1.Query<User>()
                    .OrderBy(x => x.FirstName)
                    .Where(x => x.FirstName == "nobody")
                    .Select(x => x.FirstName)
                    .FirstOrDefault();
            var single = batch1.Query<User>().Where(x => x.FirstName == "Tamba").Select(x => x.FirstName).Single();
            var singleOrDefault =
                batch1.Query<User>().Where(x => x.FirstName == "nobody").Select(x => x.FirstName).SingleOrDefault();

            await batch1.Execute();

            (await toList).ShouldHaveTheSameElementsAs("Derrick", "Dontari", "Eric", "Justin", "Sean", "Tamba");
            (await first).ShouldBe("Derrick");
            SpecificationExtensions.ShouldBeNull((await firstOrDefault));
            (await single).ShouldBe("Tamba");
            SpecificationExtensions.ShouldBeNull((await singleOrDefault));
        }

        [Fact]
        public async Task can_use_select_transformations_to_anonymous_type_in_batch()
        {
            var batch1 = theSession.CreateBatchQuery();

            var toList = batch1.Query<User>().OrderBy(x => x.FirstName).Select(x => new {Name = x.FirstName}).ToList();
            var first = batch1.Query<User>().OrderBy(x => x.FirstName).Select(x => new {Name = x.FirstName}).First();
            var firstOrDefault =
                batch1.Query<User>()
                    .OrderBy(x => x.FirstName)
                    .Where(x => x.FirstName == "nobody")
                    .Select(x => new {Name = x.FirstName})
                    .FirstOrDefault();
            var single =
                batch1.Query<User>().Where(x => x.FirstName == "Tamba").Select(x => new {Name = x.FirstName}).Single();
            var singleOrDefault =
                batch1.Query<User>()
                    .Where(x => x.FirstName == "nobody")
                    .Select(x => new {Name = x.FirstName})
                    .SingleOrDefault();

            await batch1.Execute();

            (await toList).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Derrick", "Dontari", "Eric", "Justin", "Sean", "Tamba");
            (await first).Name.ShouldBe("Derrick");
            SpecificationExtensions.ShouldBeNull((await firstOrDefault));
            (await single).Name.ShouldBe("Tamba");
            SpecificationExtensions.ShouldBeNull((await singleOrDefault));
        }


        [Fact]
        public async Task can_use_select_transformations_to_another_type_in_batch()
        {
            var batch1 = theSession.CreateBatchQuery();

            var toList =
                batch1.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName {Name = x.FirstName}).ToList();
            var first =
                batch1.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName {Name = x.FirstName}).First();
            var firstOrDefault =
                batch1.Query<User>()
                    .OrderBy(x => x.FirstName)
                    .Where(x => x.FirstName == "nobody")
                    .Select(x => new UserName {Name = x.FirstName})
                    .FirstOrDefault();
            var single =
                batch1.Query<User>()
                    .Where(x => x.FirstName == "Tamba")
                    .Select(x => new UserName {Name = x.FirstName})
                    .Single();
            var singleOrDefault =
                batch1.Query<User>()
                    .Where(x => x.FirstName == "nobody")
                    .Select(x => new UserName {Name = x.FirstName})
                    .SingleOrDefault();

            await batch1.Execute();

            (await toList).Select(x => x.Name)
                .ShouldHaveTheSameElementsAs("Derrick", "Dontari", "Eric", "Justin", "Sean", "Tamba");
            (await first).Name.ShouldBe("Derrick");
            SpecificationExtensions.ShouldBeNull((await firstOrDefault));
            (await single).Name.ShouldBe("Tamba");
            SpecificationExtensions.ShouldBeNull((await singleOrDefault));
        }


        public async Task batch_samples()
        {
            #region sample_using-batch-query
            // Start a new IBatchQuery from an active session
            var batch = theSession.CreateBatchQuery();

            // Fetch a single document by its Id
            var user1 = batch.Load<User>("username");

            // Fetch multiple documents by their id's
            var admins = batch.LoadMany<User>().ById("user2", "user3");

            // User-supplied sql
            var toms = batch.Query<User>("where first_name == ?", "Tom");

            // Where with Linq
            var jills = batch.Query<User>().Where(x => x.FirstName == "Jill").ToList();

            // Any() queries
            var anyBills = batch.Query<User>().Any(x => x.FirstName == "Bill");

            // Count() queries
            var countJims = batch.Query<User>().Count(x => x.FirstName == "Jim");

            // The Batch querying supports First/FirstOrDefault/Single/SingleOrDefault() selectors:
            var firstInternal = batch.Query<User>().OrderBy(x => x.LastName).First(x => x.Internal);

            // Kick off the batch query
            await batch.Execute();

            // All of the query mechanisms of the BatchQuery return
            // Task's that are completed by the Execute() method above
            var internalUser = await firstInternal;
            Debug.WriteLine($"The first internal user is {internalUser.FirstName} {internalUser.LastName}");
            #endregion
        }
    }
}
