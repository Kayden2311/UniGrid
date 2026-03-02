package com.unigrid.DataAccess.entity;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "ChatRoom", schema = "dbo")
public class ChatRoom {
    @Id
    @ColumnDefault("newsequentialid()")
    @Column(name = "roomId", nullable = false)
    private UUID id;

    @Nationalized
    @Column(name = "name", length = 100)
    private String name;

    @Column(name = "workspaceId")
    private UUID workspaceId;

    @Nationalized
    @Column(name = "type", length = 30)
    private String type;

    @ColumnDefault("sysdatetime()")
    @Column(name = "createdAt")
    private Instant createdAt;


}