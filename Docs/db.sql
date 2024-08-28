----------------TABLE [sys_user] BEGIN----------------
--DROP TABLE IF EXISTS "sys_user";
CREATE TABLE "sys_user"
(
    "user_id" VARCHAR(50) NOT NULL,
    "user_name" VARCHAR(50) NOT NULL,
    "account" VARCHAR(50) NOT NULL,
    "mobile" VARCHAR(50) NULL,
    "email" VARCHAR(100) NULL,
    "tenant_id" VARCHAR(50) NULL,
    "password" VARCHAR(100) NULL,
    "gender" INTEGER NULL,
    "birth_date" VARCHAR(50) NULL,
    "salt" VARCHAR(50) NULL,
    "locked_end" TIMESTAMP NULL,
    "status" INTEGER NOT NULL DEFAULT 1,
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_user PRIMARY KEY("user_id")
);

COMMENT ON COLUMN "sys_user"."user_id" IS '用户ID';
COMMENT ON COLUMN "sys_user"."user_name" IS '用户名称';
COMMENT ON COLUMN "sys_user"."account" IS '登录账号';
COMMENT ON COLUMN "sys_user"."mobile" IS '手机号码';
COMMENT ON COLUMN "sys_user"."email" IS '邮箱';
COMMENT ON COLUMN "sys_user"."tenant_id" IS '租户ID';
COMMENT ON COLUMN "sys_user"."password" IS '密码';
COMMENT ON COLUMN "sys_user"."gender" IS '性别';
COMMENT ON COLUMN "sys_user"."birth_date" IS '生日';
COMMENT ON COLUMN "sys_user"."salt" IS '盐';
COMMENT ON COLUMN "sys_user"."locked_end" IS '解锁时间';
COMMENT ON COLUMN "sys_user"."status" IS '状态';
COMMENT ON COLUMN "sys_user"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_user"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_user"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_user"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_user" IS  '用户表，描述登录时所用的所有相关信息';
----------------TABLE [sys_user] END----------------




----------------TABLE [sys_role] BEGIN----------------
--DROP TABLE IF EXISTS "sys_role";
CREATE TABLE "sys_role"
(
    "role_id" VARCHAR(50) NOT NULL,
    "role_name" VARCHAR(50) NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_role PRIMARY KEY("role_id")
);

COMMENT ON COLUMN "sys_role"."role_id" IS '角色ID';
COMMENT ON COLUMN "sys_role"."role_name" IS '角色名称';
COMMENT ON COLUMN "sys_role"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_role"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_role"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_role"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_role"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_role" IS  '角色表，描述所有角色表';
----------------TABLE [sys_role] END----------------




----------------TABLE [sys_user_role] BEGIN----------------
--DROP TABLE IF EXISTS "sys_user_role";
CREATE TABLE "sys_user_role"
(
    "user_id" VARCHAR(50) NOT NULL,
    "role_id" VARCHAR(50) NOT NULL,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_user_role PRIMARY KEY("user_id","role_id")
);

COMMENT ON COLUMN "sys_user_role"."user_id" IS '资源ID';
COMMENT ON COLUMN "sys_user_role"."role_id" IS '角色ID';
COMMENT ON COLUMN "sys_user_role"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_user_role"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_user_role" IS  '用户角色表，描述用户与角色的关联关系';
----------------TABLE [sys_user_role] END----------------




----------------TABLE [sys_resource] BEGIN----------------
--DROP TABLE IF EXISTS "sys_resource";
CREATE TABLE "sys_resource"
(
    "resource_id" VARCHAR(50) NOT NULL,
    "resource_name" VARCHAR(50) NULL,
    "resource_type" INTEGER NULL,
    "parent_id" VARCHAR(50) NULL,
    "is_link" BOOLEAN NULL,
    "route_url" VARCHAR(200) NULL,
    "action_url" VARCHAR(200) NULL,
    "component" VARCHAR(200) NULL,
    "is_full" BOOLEAN NULL DEFAULT 'f',
    "is_affix" BOOLEAN NULL DEFAULT 'f',
    "sequence" INTEGER NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_resource PRIMARY KEY("resource_id")
);

COMMENT ON COLUMN "sys_resource"."resource_id" IS '资源ID';
COMMENT ON COLUMN "sys_resource"."resource_name" IS '资源名称';
COMMENT ON COLUMN "sys_resource"."resource_type" IS '资源类型';
COMMENT ON COLUMN "sys_resource"."parent_id" IS '父亲ID';
COMMENT ON COLUMN "sys_resource"."is_link" IS '是否外部连接';
COMMENT ON COLUMN "sys_resource"."route_url" IS '路由地址';
COMMENT ON COLUMN "sys_resource"."action_url" IS '路由地址';
COMMENT ON COLUMN "sys_resource"."component" IS '组件物理路径';
COMMENT ON COLUMN "sys_resource"."is_full" IS '是否全屏显示';
COMMENT ON COLUMN "sys_resource"."is_affix" IS '是否固定标签页';
COMMENT ON COLUMN "sys_resource"."sequence" IS '排序';
COMMENT ON COLUMN "sys_resource"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_resource"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_resource"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_resource"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_resource"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_resource" IS  '资源表，包含菜单、权限按钮等资源';
----------------TABLE [sys_resource] END----------------




----------------TABLE [sys_authorization] BEGIN----------------
--DROP TABLE IF EXISTS "sys_authorization";
CREATE TABLE "sys_authorization"
(
    "role_id" VARCHAR(50) NOT NULL,
    "resource_id" VARCHAR(50) NOT NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_authorization PRIMARY KEY("role_id","resource_id")
);

COMMENT ON COLUMN "sys_authorization"."role_id" IS '角色ID';
COMMENT ON COLUMN "sys_authorization"."resource_id" IS '菜单ID';
COMMENT ON COLUMN "sys_authorization"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_authorization"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_authorization"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_authorization"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_authorization"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_authorization" IS  '授权表，描述一个角色所拥有的每个菜单项+功能按钮的关联，存在即授权';
----------------TABLE [sys_authorization] END----------------




----------------TABLE [sys_group] BEGIN----------------
--DROP TABLE IF EXISTS "sys_group";
CREATE TABLE "sys_group"
(
    "group_id" VARCHAR(50) NOT NULL,
    "group_name" VARCHAR(100) NOT NULL,
    "parent_id" VARCHAR(50) NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_group PRIMARY KEY("group_id")
);

COMMENT ON COLUMN "sys_group"."group_id" IS '组ID';
COMMENT ON COLUMN "sys_group"."group_name" IS '组名称';
COMMENT ON COLUMN "sys_group"."parent_id" IS '父亲ID';
COMMENT ON COLUMN "sys_group"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_group"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_group"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_group"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_group"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_group" IS  '组表，描述拥有指定数据权限的用户归属的数据权限组';
----------------TABLE [sys_group] END----------------




----------------TABLE [sys_data] BEGIN----------------
--DROP TABLE IF EXISTS "sys_data";
CREATE TABLE "sys_data"
(
    "data_type" VARCHAR(50) NOT NULL,
    "data_id" VARCHAR(50) NOT NULL,
    "data_value" VARCHAR(100) NOT NULL,
    "group_id" VARCHAR(50) NOT NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_data PRIMARY KEY("data_type","data_id")
);

COMMENT ON COLUMN "sys_data"."data_type" IS '数据类型';
COMMENT ON COLUMN "sys_data"."data_id" IS '数据ID';
COMMENT ON COLUMN "sys_data"."data_value" IS '数据值';
COMMENT ON COLUMN "sys_data"."group_id" IS '组ID';
COMMENT ON COLUMN "sys_data"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_data"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_data"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_data"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_data"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_data" IS  '数据表，描述数据权限的权限数据';
----------------TABLE [sys_data] END----------------




----------------TABLE [sys_user_group] BEGIN----------------
--DROP TABLE IF EXISTS "sys_user_group";
CREATE TABLE "sys_user_group"
(
    "group_id" VARCHAR(50) NOT NULL,
    "user_id" VARCHAR(100) NOT NULL,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_user_group PRIMARY KEY("group_id","user_id")
);

COMMENT ON COLUMN "sys_user_group"."group_id" IS '组ID';
COMMENT ON COLUMN "sys_user_group"."user_id" IS '组名称';
COMMENT ON COLUMN "sys_user_group"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_user_group"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_user_group" IS  '用户组表，描述每个用户归属哪个数据权限组';
----------------TABLE [sys_user_group] END----------------





----------------TABLE [sys_lookup] BEGIN----------------
--DROP TABLE IF EXISTS "sys_lookup";
CREATE TABLE "sys_lookup"
(
    "lookup_id" VARCHAR(50) NOT NULL,
    "lookup_name" VARCHAR(50) NULL,
    "description" VARCHAR(100) NULL,
    "parent_id" VARCHAR(50) NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_lookup PRIMARY KEY("lookup_id")
);

COMMENT ON COLUMN "sys_lookup"."lookup_id" IS '参数ID';
COMMENT ON COLUMN "sys_lookup"."lookup_name" IS '参数名称';
COMMENT ON COLUMN "sys_lookup"."description" IS '描述';
COMMENT ON COLUMN "sys_lookup"."parent_id" IS '父亲ID';
COMMENT ON COLUMN "sys_lookup"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_lookup"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_lookup"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_lookup"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_lookup"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_lookup" IS  '参数表，描述系统中所有的类型，种类等定义';
----------------TABLE [sys_lookup] END----------------




----------------TABLE [sys_lookup_value] BEGIN----------------
--DROP TABLE IF EXISTS "sys_lookup_value";
CREATE TABLE "sys_lookup_value"
(
    "lookup_id" VARCHAR(50) NOT NULL,
    "lookup_value" VARCHAR(50) NULL,
    "lookup_text" VARCHAR(50) NULL,
    "description" VARCHAR(100) NULL,
    "sequence" INTEGER NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_sys_lookup_value PRIMARY KEY("lookup_id","lookup_value")
);

COMMENT ON COLUMN "sys_lookup_value"."lookup_id" IS '参数ID';
COMMENT ON COLUMN "sys_lookup_value"."lookup_value" IS '参数值';
COMMENT ON COLUMN "sys_lookup_value"."lookup_text" IS '参数文本';
COMMENT ON COLUMN "sys_lookup_value"."description" IS '描述';
COMMENT ON COLUMN "sys_lookup_value"."sequence" IS '排序';
COMMENT ON COLUMN "sys_lookup_value"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "sys_lookup_value"."created_by" IS '创建人';
COMMENT ON COLUMN "sys_lookup_value"."created_at" IS '创建日期';
COMMENT ON COLUMN "sys_lookup_value"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "sys_lookup_value"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "sys_lookup_value" IS  '参数值表，描述系统中所有的类型，种类等有限集合字典数据';
----------------TABLE [sys_lookup_value] END----------------




----------------TABLE [mds_cluster] BEGIN----------------
--DROP TABLE IF EXISTS "mds_cluster";
CREATE TABLE "mds_cluster"
(
    "cluster_id" VARCHAR(50) NOT NULL,
    "cluster_name" VARCHAR(50) NULL,
    "url" VARCHAR(100) NULL,
    "user" VARCHAR(100) NULL,
    "password" VARCHAR(100) NULL,
    "bind_type" VARCHAR(50) NULL,
    "is_stateful" BOOLEAN NULL DEFAULT 'f',
    "is_log_enabled" BOOLEAN NULL DEFAULT 'f',
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_mds_cluster PRIMARY KEY("cluster_id")
);

COMMENT ON COLUMN "mds_cluster"."cluster_id" IS '集群ID';
COMMENT ON COLUMN "mds_cluster"."cluster_name" IS '集群名称';
COMMENT ON COLUMN "mds_cluster"."url" IS '连接URL';
COMMENT ON COLUMN "mds_cluster"."user" IS '用户名';
COMMENT ON COLUMN "mds_cluster"."password" IS '密码';
COMMENT ON COLUMN "mds_cluster"."bind_type" IS '绑定类型';
COMMENT ON COLUMN "mds_cluster"."is_stateful" IS '是否有状态';
COMMENT ON COLUMN "mds_cluster"."is_log_enabled" IS '是否开启日志';
COMMENT ON COLUMN "mds_cluster"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "mds_cluster"."created_by" IS '创建人';
COMMENT ON COLUMN "mds_cluster"."created_at" IS '创建日期';
COMMENT ON COLUMN "mds_cluster"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "mds_cluster"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "mds_cluster" IS  '集群表，描述消息队列的一个集群基本信息';
----------------TABLE [mds_cluster] END----------------




----------------TABLE [mds_binding] BEGIN----------------
--DROP TABLE IF EXISTS "mds_binding";
CREATE TABLE "mds_binding"
(
    "bingding_id" VARCHAR(50) NOT NULL,
    "cluster_id" VARCHAR(50) NULL,
    "exchange" VARCHAR(50) NULL,
    "queue" VARCHAR(50) NULL,
    "bind_type" VARCHAR(50) NULL,
    "binding_key" VARCHAR(50) NULL,
    "host_name" VARCHAR(100) NULL,
    "prefetch_count" INTEGER NULL,
    "is_single_active_consumer" BOOLEAN NOT NULL DEFAULT 'f',
    "is_reply" BOOLEAN NOT NULL DEFAULT 'f',
    "is_delay" BOOLEAN NOT NULL DEFAULT 'f',
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_mds_binding PRIMARY KEY("bingding_id")
);

COMMENT ON COLUMN "mds_binding"."bingding_id" IS '绑定ID';
COMMENT ON COLUMN "mds_binding"."cluster_id" IS '集群ID';
COMMENT ON COLUMN "mds_binding"."exchange" IS '信箱';
COMMENT ON COLUMN "mds_binding"."queue" IS '队列';
COMMENT ON COLUMN "mds_binding"."bind_type" IS '绑定类型';
COMMENT ON COLUMN "mds_binding"."binding_key" IS '绑定KEY';
COMMENT ON COLUMN "mds_binding"."host_name" IS '主机名称';
COMMENT ON COLUMN "mds_binding"."prefetch_count" IS '预取个数';
COMMENT ON COLUMN "mds_binding"."is_single_active_consumer" IS '是否单一激活消费者';
COMMENT ON COLUMN "mds_binding"."is_reply" IS '是否应答队列';
COMMENT ON COLUMN "mds_binding"."is_delay" IS '是否延时消费者';
COMMENT ON COLUMN "mds_binding"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "mds_binding"."created_by" IS '创建人';
COMMENT ON COLUMN "mds_binding"."created_at" IS '创建日期';
COMMENT ON COLUMN "mds_binding"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "mds_binding"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "mds_binding" IS  '绑定信息表，描述消息队列每个消费者的绑定关系';
----------------TABLE [mds_binding] END----------------




----------------TABLE [mds_log] BEGIN----------------
--DROP TABLE IF EXISTS "mds_log";
CREATE TABLE "mds_log"
(
    "log_id" VARCHAR(50) NOT NULL,
    "cluster_id" VARCHAR(50) NULL,
    "routing_key" VARCHAR(50) NULL,
    "queue" VARCHAR(50) NULL,
    "body" VARCHAR(50) NULL,
    "is_success" BOOLEAN NULL DEFAULT 'f',
    "result" VARCHAR(4000) NULL,
    "retry_times" INTEGER NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_mds_log PRIMARY KEY("log_id")
);

COMMENT ON COLUMN "mds_log"."log_id" IS '日志ID';
COMMENT ON COLUMN "mds_log"."cluster_id" IS '集群ID';
COMMENT ON COLUMN "mds_log"."routing_key" IS '路由KEY';
COMMENT ON COLUMN "mds_log"."queue" IS '队列';
COMMENT ON COLUMN "mds_log"."body" IS '消息内容';
COMMENT ON COLUMN "mds_log"."is_success" IS '是否成功';
COMMENT ON COLUMN "mds_log"."result" IS '执行结果';
COMMENT ON COLUMN "mds_log"."retry_times" IS '重试次数';
COMMENT ON COLUMN "mds_log"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "mds_log"."created_by" IS '创建人';
COMMENT ON COLUMN "mds_log"."created_at" IS '创建日期';
COMMENT ON COLUMN "mds_log"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "mds_log"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "mds_log" IS  '日志表，描述消息队列每个消费者的执行日志';
----------------TABLE [mds_log] END----------------




----------------TABLE [res_rule] BEGIN----------------
--DROP TABLE IF EXISTS "res_rule";
CREATE TABLE "res_rule"
(
    "rule_id" VARCHAR(50) NOT NULL,
    "description" VARCHAR(500) NULL,
    "expression" VARCHAR(200) NULL,
    "parameters" VARCHAR(200) NULL,
    "completion_type" INTEGER NULL DEFAULT 1,
    "warn_type" INTEGER NULL DEFAULT 0,
    "warn_message" VARCHAR(500) NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_res_rule PRIMARY KEY("rule_id")
);

COMMENT ON COLUMN "res_rule"."rule_id" IS '规则ID';
COMMENT ON COLUMN "res_rule"."description" IS '规则描述';
COMMENT ON COLUMN "res_rule"."expression" IS '规则表达式';
COMMENT ON COLUMN "res_rule"."parameters" IS '参数名列表';
COMMENT ON COLUMN "res_rule"."completion_type" IS '完成类型';
COMMENT ON COLUMN "res_rule"."warn_type" IS '警告类型';
COMMENT ON COLUMN "res_rule"."warn_message" IS '警告内容';
COMMENT ON COLUMN "res_rule"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "res_rule"."created_by" IS '创建人';
COMMENT ON COLUMN "res_rule"."created_at" IS '创建日期';
COMMENT ON COLUMN "res_rule"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "res_rule"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "res_rule" IS  '规则表，描述所有可执行的规则';
----------------TABLE [res_rule] END----------------





----------------TABLE [res_rule_parameter] BEGIN----------------
--DROP TABLE IF EXISTS "res_rule_parameter";
CREATE TABLE "res_rule_parameter"
(
    "parameter_id" VARCHAR(50) NOT NULL,
    "type_name" VARCHAR(50) NULL,
    "description" VARCHAR(200) NULL,
    "service_name" VARCHAR(100) NULL,
    "is_enabled" BOOLEAN NOT NULL DEFAULT 't',
    "created_by" VARCHAR(50) NOT NULL,
    "created_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_by" VARCHAR(50) NOT NULL,
    "updated_at" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_res_rule_parameter PRIMARY KEY("parameter_id")
);

COMMENT ON COLUMN "res_rule_parameter"."parameter_id" IS '参数ID';
COMMENT ON COLUMN "res_rule_parameter"."type_name" IS '类型名称';
COMMENT ON COLUMN "res_rule_parameter"."description" IS '描述';
COMMENT ON COLUMN "res_rule_parameter"."service_name" IS '获取服务名称';
COMMENT ON COLUMN "res_rule_parameter"."is_enabled" IS '是否启用';
COMMENT ON COLUMN "res_rule_parameter"."created_by" IS '创建人';
COMMENT ON COLUMN "res_rule_parameter"."created_at" IS '创建日期';
COMMENT ON COLUMN "res_rule_parameter"."updated_by" IS '最后更新人';
COMMENT ON COLUMN "res_rule_parameter"."updated_at" IS '最后更新日期';
COMMENT ON TABLE "res_rule_parameter" IS  '规则参数表，描述所有规则参数的定义 ';
----------------TABLE [res_rule_parameter] END----------------
