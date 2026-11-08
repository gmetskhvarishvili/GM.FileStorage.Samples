using GM.EntityFramework.Persistence.Repositories;
using GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate;
using GM.FileStorage.Sample.Domain.BoundedContext.AuditBoundedContext.AuditAggregate.Interfaces;
using GM.FileStorage.Sample.Persistence.Context;

namespace GM.FileStorage.Sample.Persistence.Repositories;

public class DomainEventLogRepository(ApplicationDbContext context)
    : GenericRepository<DomainEventLog, ApplicationDbContext>(context), IDomainEventLogRepository;
