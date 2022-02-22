﻿using NUnit.Framework;
using Smartstore.Core.Identity;
using Smartstore.Test.Common;

namespace Smartstore.Core.Tests.Platform.Identity
{
    [TestFixture]
    public class CustomerExtensionTests
    {
        [SetUp]
        public void SetUp()
        {
        }

        [Test]
        public void Can_check_IsInCustomerRole()
        {
            var customer = new Customer();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Test name 1",
                    SystemName = "Test system name 1",
                }
            });
            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = false,
                    Name = "Test name 2",
                    SystemName = "Test system name 2",
                }
            });

            customer.IsInRole("Test system name 1", false).ShouldBeTrue();
            customer.IsInRole("Test system name 1", true).ShouldBeTrue();

            customer.IsInRole("Test system name 2", false).ShouldBeTrue();
            customer.IsInRole("Test system name 2", true).ShouldBeFalse();

            customer.IsInRole("Test system name 3", false).ShouldBeFalse();
            customer.IsInRole("Test system name 3", true).ShouldBeFalse();
        }

        [Test]
        public void Can_check_whether_customer_is_admin()
        {
            var customer = new Customer();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Registered",
                    SystemName = SystemCustomerRoleNames.Registered
                }
            });

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Guests",
                    SystemName = SystemCustomerRoleNames.Guests
                }
            });

            customer.IsAdmin().ShouldBeFalse();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Administrators",
                    SystemName = SystemCustomerRoleNames.Administrators
                }
            });

            customer.IsAdmin().ShouldBeTrue();
        }

        [Test]
        public void Can_check_whether_customer_is_forum_moderator()
        {
            var customer = new Customer();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Registered",
                    SystemName = SystemCustomerRoleNames.Registered
                }
            });

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Guests",
                    SystemName = SystemCustomerRoleNames.Guests
                }
            });

            customer.IsInRole("ForumModerators").ShouldBeFalse();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "ForumModerators",
                    SystemName = SystemCustomerRoleNames.ForumModerators
                }
            });

            customer.IsInRole("ForumModerators").ShouldBeTrue();
        }

        [Test]
        public void Can_check_whether_customer_is_guest()
        {
            var customer = new Customer();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Registered",
                    SystemName = SystemCustomerRoleNames.Registered
                }
            });

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Administrators",
                    SystemName = SystemCustomerRoleNames.Administrators
                }
            });

            customer.IsGuest().ShouldBeFalse();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Guests",
                    SystemName = SystemCustomerRoleNames.Guests
                }
            });

            customer.IsGuest().ShouldBeTrue();
        }

        [Test]
        public void Can_check_whether_customer_is_registered()
        {
            var customer = new Customer();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Administrators",
                    SystemName = SystemCustomerRoleNames.Administrators
                }
            });

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Guests",
                    SystemName = SystemCustomerRoleNames.Guests
                }
            });

            customer.IsRegistered().ShouldBeFalse();

            customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerRole = new CustomerRole
                {
                    Active = true,
                    Name = "Registered",
                    SystemName = SystemCustomerRoleNames.Registered
                }
            });

            customer.IsRegistered().ShouldBeTrue();
        }
    }
}
