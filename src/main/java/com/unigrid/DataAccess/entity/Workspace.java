package com.unigrid.DataAccess.entity;

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
@Table(name = "Workspace", schema = "dbo")
public class Workspace {
    @Id
    @Column(name = "workspaceId", nullable = false)
    private UUID id;

    @Nationalized
    @Column(name = "name", nullable = false, length = 100)
    private String name;

    @Nationalized
    @Lob
    @Column(name = "description")
    private String description;

    @Column(name = "createdBy", nullable = false)
    private UUID createdBy;

    @ColumnDefault("0")
    @Column(name = "isArchived")
    private Boolean isArchived;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt")
    private Instant createdAt;

    @Column(name = "updatedAt")
    private Instant updatedAt;

    @ColumnDefault("0")
    @Column(name = "totalSpace")
    private Long totalSpace;

    @ColumnDefault("0")
    @Column(name = "usedSpace")
    private Long usedSpace;

    @ColumnDefault("0")
    @Column(name = "tier")
    private Integer tier;


}