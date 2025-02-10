-- -------------- TABLE [mds_cluster] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_cluster`;
CREATE TABLE `mds_cluster`
(
    `ClusterId` VARCHAR(50) NOT NULL COMMENT '集群ID',
    `ClusterName` VARCHAR(50) NULL COMMENT '集群名称',
    `Exchange` VARCHAR(100) NULL COMMENT '信箱',
    `BindType` VARCHAR(50) NULL COMMENT '绑定类型',    
    `BindingKey` VARCHAR(50) NULL COMMENT '绑定Key',
    `Queue` VARCHAR(50) NULL COMMENT '队列名字或是队列前缀',
    `WorkloadTotal` INT NULL DEFAULT 2 COMMENT '工作负荷个数',
    `IsStateful` TINYINT(1) NULL DEFAULT 1 COMMENT '是否有状态',
    `IsSac` TINYINT(1) NULL DEFAULT 1 COMMENT '是否单一激活消费者',
    `IsDelay` TINYINT(1) NULL DEFAULT 1 COMMENT '是否延迟消息',
    `IsLogEnabled` TINYINT(1) NULL DEFAULT 1 COMMENT '是否启用日志',
    `PrefetchCount` INT NULL DEFAULT 5 COMMENT '预取消息个数',
    `IsEnabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    `UpdatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '最后更新日期',
    PRIMARY KEY(`ClusterId`)
);
ALTER TABLE `mds_cluster` COMMENT '集群表，描述消息驱动所有的业务集群，一个业务一个集群';
-- -------------- TABLE [mds_cluster] END----------------



-- -------------- TABLE [mds_log] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_log`;
CREATE TABLE `mds_log`
(
    `LogId` VARCHAR(50) NOT NULL COMMENT '日志ID',
    `ClusterId` VARCHAR(50) NULL COMMENT '集群ID',
    `RoutingKey` VARCHAR(50) NULL COMMENT '路由',
    `Queue` VARCHAR(50) NULL COMMENT '队列名称',
    `Body` VARCHAR(4000) NULL COMMENT '消息内容',
    `IsSuccess` TINYINT(1) NULL COMMENT '是否成功 ',
    `Result` TEXT NULL COMMENT '返回值',
    `RetryTimes` INT NULL COMMENT '重试次数',
    `UpdatedBy` VARCHAR(50) NOT NULL COMMENT '最后更新人',
    `UpdatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '最后更新日期',
    PRIMARY KEY(`LogId`)
);
ALTER TABLE `mds_log` COMMENT '消息驱动日志表';
-- -------------- TABLE [mds_log] END----------------