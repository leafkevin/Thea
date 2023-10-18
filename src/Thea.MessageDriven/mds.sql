-- -------------- TABLE [mds_cluster] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_cluster`;
CREATE TABLE `mds_cluster`
(
    `ClusterId` VARCHAR(50) NOT NULL COMMENT '集群ID',
    `ClusterName` VARCHAR(50) NULL COMMENT '集群名称',
    `BindType` VARCHAR(50) NULL COMMENT '绑定类型',
    `Url` VARCHAR(100) NULL COMMENT '连接URL',
    `User` VARCHAR(50) NULL COMMENT '用户名',
    `Password` VARCHAR(50) NULL COMMENT '密码',
    `IsStateful` TINYINT(1) NULL DEFAULT 1 COMMENT '是否有状态',
    `IsLogEnabled` TINYINT(1) NULL DEFAULT 1 COMMENT '是否启用日志',
    `IsEnabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    `CreatedBy` VARCHAR(50) NOT NULL COMMENT '创建人',
    `CreatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '创建日期',
    `UpdatedBy` VARCHAR(50) NOT NULL COMMENT '最后更新人',
    `UpdatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '最后更新日期',
    PRIMARY KEY(`ClusterId`)
);
ALTER TABLE `mds_cluster` COMMENT '集群表，描述消息驱动所有的业务集群，一个业务一个集群';
-- -------------- TABLE [mds_cluster] END----------------


-- -------------- TABLE [mds_binding] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_binding`;
CREATE TABLE `mds_binding`
(
    `BindingId` VARCHAR(50) NOT NULL COMMENT '绑定ID',
    `ClusterId` VARCHAR(50) NULL COMMENT '集群ID',
    `Exchange` VARCHAR(50) NULL COMMENT '信箱',
    `Queue` VARCHAR(50) NULL COMMENT '队列',
    `BindType` VARCHAR(50) NULL COMMENT '绑定类型',
    `BindingKey` VARCHAR(100) NULL COMMENT '绑定Key',
    `IsReply` TINYINT(1) NULL COMMENT '是否应答队列',
    `IsDelay` TINYINT(1) NULL COMMENT '是否延时交换机',
    `PrefetchCount` INT NULL COMMENT '预取消息个数',
    `IsSingleActiveConsumer` TINYINT(1) NULL COMMENT '是否单一激活消费者',
    `IsEnabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    `CreatedBy` VARCHAR(50) NOT NULL COMMENT '创建人',
    `CreatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '创建日期',
    `UpdatedBy` VARCHAR(50) NOT NULL COMMENT '最后更新人',
    `UpdatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '最后更新日期',
    PRIMARY KEY(`BindingId`)
);
ALTER TABLE `mds_binding` COMMENT '队列绑定表，描述消息驱动所有的业务队列与信箱的绑定关系';
-- -------------- TABLE [mds_binding] END----------------


-- -------------- TABLE [mds_exec_log] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_exec_log`;
CREATE TABLE `mds_exec_log`
(
    `LogId` VARCHAR(50) NOT NULL COMMENT '日志ID',
    `ClusterId` VARCHAR(50) NULL COMMENT '集群ID',
    `RoutingKey` VARCHAR(50) NULL COMMENT '路由',
    `Queue` VARCHAR(50) NULL COMMENT '队列名称',
    `Body` VARCHAR(4000) NULL COMMENT '消息内容',
    `IsSuccess` TINYINT(1) NULL COMMENT '是否成功 ',
    `Result` TEXT NULL COMMENT '返回值',
    `RetryTimes` INT NULL COMMENT '重试次数',
    `CreatedBy` VARCHAR(50) NOT NULL COMMENT '创建人',
    `CreatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '创建日期',
    `UpdatedBy` VARCHAR(50) NOT NULL COMMENT '最后更新人',
    `UpdatedAt` DATETIME NOT NULL DEFAULT NOW() COMMENT '最后更新日期',
    PRIMARY KEY(`LogId`)
);
ALTER TABLE `mds_exec_log` COMMENT '消息驱动日志表';
-- -------------- TABLE [mds_exec_log] END----------------