# Test Matrix

Maps product behavior to proof. Do not mark a row `implemented` until tests
or validation evidence exist.

## Status Values

| Status | Meaning |
| --- | --- |
| planned | Accepted as intended behavior, not implemented |
| in_progress | Actively being built |
| implemented | Implemented and proof exists |
| changed | Contract changed after earlier implementation |
| retired | No longer part of the product contract |

## Matrix

| Feature | Behavior | Unit | Integration | E2E | Status |
| --- | --- | --- | --- | --- | --- |
| **Products** | CRUD operations | ✅ | ❌ | ❌ | implemented |
| Products | Search products (unified) | ✅ | ❌ | ❌ | implemented |
| Products | Update product | ✅ | ❌ | ❌ | implemented |
| **ProductCategories** | CRUD operations | ✅ | ❌ | ❌ | implemented |
| **Posts** | CRUD operations | ✅ | ❌ | ❌ | implemented |
| Posts | Approve post | ✅ | ❌ | ❌ | implemented |
| Posts | Post crawler service | ✅ | ❌ | ❌ | implemented |
| **PostCategories** | CRUD operations | ❌ | ❌ | ❌ | planned |
| **Orders** | CRUD operations | ✅ | ❌ | ❌ | implemented |
| Orders | Order status workflow | ❌ | ❌ | ❌ | planned |
| **Cart** | Add/remove/get items | ✅ | ❌ | ❌ | implemented |
| **Auth** | Login/Register | ❌ | ❌ | ❌ | planned |
| Auth | JWT token + refresh | ❌ | ❌ | ❌ | planned |
| **Users** | Profile management | ❌ | ❌ | ❌ | planned |
| **Payments** | Payment transactions | ❌ | ❌ | ❌ | planned |
| **Chat** | Real-time messaging | ❌ | ❌ | ❌ | planned |
| **Notifications** | Push notifications | ❌ | ❌ | ❌ | planned |
| **Favorites** | Add/remove favorites | ❌ | ❌ | ❌ | planned |
| **Reviews** | Product reviews | ❌ | ❌ | ❌ | planned |
| **WebsiteInfo** | Get all website info | ✅ | ❌ | ❌ | implemented |
| **Files** | File upload/management | ❌ | ❌ | ❌ | planned |
| **Architecture** | Architecture tests | ✅ | — | — | implemented |
| **API Contract** | OpenAPI spec compliance | ✅ | — | — | implemented |

## Coverage Summary

- **Unit tests**: Products, Posts, Orders, Cart, WebsiteInfo, Architecture, API Contract
- **Integration tests**: None
- **E2E tests**: None
- **Major gaps**: Auth, Payments, Chat, Notifications, Users — no test coverage

## How to Update

When completing work on a feature:
1. Update the row status to `implemented`.
2. Mark which test levels have proof (✅).
3. If changing existing behavior, mark status as `changed`.
4. Run `dotnet test` to confirm all proof is green.
