-- -------------- TABLE [mds_setting] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_setting`;
CREATE TABLE `mds_setting`
(
    `queue` VARCHAR(50) NULL COMMENT '队列名称',
    `exchanges` VARCHAR(300) NULL COMMENT '交换机',
    `bind_type` VARCHAR(50) NULL COMMENT '绑定类型',
    `binding_key` VARCHAR(50) NULL COMMENT '绑定KEY',
    `is_quorum_queue` TINYINT(1) NULL DEFAULT 1 COMMENT '是否仲裁队列',
    `is_stateful` TINYINT(1) NULL DEFAULT 0 COMMENT '是否有状态',
    `is_single_active_consumer` TINYINT(1) NULL DEFAULT 0 COMMENT '是否单一激活消费者',
    `is_need_transfer` TINYINT(1) NULL DEFAULT 0 COMMENT '是否需要转发',
    `is_delay` TINYINT(1) NULL DEFAULT 0 COMMENT '是否延迟消息',
    `workload_total` INT NULL COMMENT '工作负荷个数',
    `prefetch_count` INT NULL DEFAULT 250 COMMENT '预取个数',
    `is_log_enabled` TINYINT(1) NULL DEFAULT 0 COMMENT '是否开启日志',
    `is_enabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    `created_by` VARCHAR(50) NULL COMMENT '创建人',
    `created_at` DATETIME NULL DEFAULT NOW() COMMENT '创建日期',
    `updated_by` VARCHAR(50) NULL COMMENT '最后更新人',
    `updated_at` DATETIME NULL DEFAULT NOW() COMMENT '最后更新日期',
    CONSTRAINT `pk_mds_setting` PRIMARY KEY(`queue`)
);
ALTER TABLE `mds_setting` COMMENT '配置表，描述所有的队列、交换机绑定等信息';
-- -------------- TABLE [mds_setting] END----------------


-- -------------- TABLE [mds_log] BEGIN----------------
-- DROP TABLE IF EXISTS `mds_log`;
CREATE TABLE `mds_log`
(
    `log_id` VARCHAR(50) NOT NULL COMMENT '日志ID',
    `trace_id` VARCHAR(50) NULL COMMENT '跟踪ID',
    `exchange` VARCHAR(50) NULL COMMENT '交换机',
    `queue` VARCHAR(50) NULL COMMENT '队列名称',
    `routing_key` VARCHAR(50) NULL COMMENT '路由KEY',
    `body` VARCHAR(50) NULL COMMENT '消息内容',
    `is_success` TINYINT(1) NULL DEFAULT 0 COMMENT '是否成功',
    `result` VARCHAR(4000) NULL COMMENT '执行结果',
    `retry_times` INT NULL COMMENT '重试次数',
    `updated_by` VARCHAR(50) NULL COMMENT '最后更新人',
    `updated_at` DATETIME NULL DEFAULT NOW() COMMENT '最后更新日期',
    CONSTRAINT `pk_mds_log` PRIMARY KEY(`log_id`)
);
ALTER TABLE `mds_log` COMMENT '日志表，描述消息队列每个消费者的执行日志';
-- -------------- TABLE [mds_log] END----------------