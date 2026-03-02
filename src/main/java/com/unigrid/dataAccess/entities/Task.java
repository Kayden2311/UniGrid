package com.unigrid.dataAccess.entities;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Task", schema = "dbo")
public class Task {
    @Id
    @Column(name = "taskId", nullable = false)
    private UUID id;

    @Nationalized
    @Column(name = "title", nullable = false)
    private String title;

    @Nationalized
    @Lob
    @Column(name = "description")
    private String description;

    @Nationalized
    @Column(name = "status", length = 30)
    private String status;

    @Column(name = "priority")
    private Integer priority;

    @Column(name = "dueDate")
    private Instant dueDate;

    @Column(name = "createdBy", nullable = false)
    private UUID createdBy;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt")
    private Instant createdAt;

    @Column(name = "updatedAt")
    private Instant updatedAt;

    @Column(name = "workspaceId", nullable = false)
    private UUID workspaceId;

    @Column(name = "scheduleId")
    private UUID scheduleId;


}